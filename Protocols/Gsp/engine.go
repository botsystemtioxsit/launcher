package main

import (
	"encoding/binary"
	"errors"
	"fmt"
	"net"
	"net/netip"
	"sync"
	"sync/atomic"
	"time"

	"github.com/flynn/noise"
	"golang.org/x/net/ipv4"
)

// Device — виртуальная сетевая карта (TUN). Те же методы, что у golang.zx2c4.com/wireguard/tun,
// поэтому в тестах подставляется простая подделка.
type Device interface {
	Read(bufs [][]byte, sizes []int, offset int) (int, error)
	Write(bufs [][]byte, offset int) (int, error)
	BatchSize() int
	Close() error
}

type PeerConfig struct {
	PublicKey []byte
	Endpoint  netip.AddrPort // пусто у сервера: клиент сам объявится
	Allowed   []netip.Prefix
	Keepalive time.Duration
}

type Config struct {
	PrivateKey []byte
	ListenPort int // 0 — любой (у клиента)
	Peers      []PeerConfig
	FEC        string // "off" | "auto" | "10+2"
	PadMax     int    // случайная набивка до N байт
	Proto      int    // версия протокола у клиента: 1 (по умолчанию) или 2; сервер принимает обе
	Debug      bool
	// Для тестов и сравнений: искусственные потери исходящих пакетов (доля 0..1).
	DebugDropTx float64
}

type peer struct {
	e         *Engine
	pub       [32]byte
	ver       int      // версия протокола, на которой клиент начинает рукопожатие (у сервера не используется: он принимает обе)
	obf       [32]byte // ключ обфускации рукопожатия (из открытого ключа СЕРВЕРА — того, к кому идёт init)
	allowed   []netip.Prefix
	keepalive time.Duration
	endpoint  atomic.Pointer[net.UDPAddr]
	fixedEP   bool // адрес задан в настройках — значит, рукопожатие начинаем мы

	cur, prev atomic.Pointer[session]
	mu        sync.Mutex
	pending   *pendingHS
	lastTS    [12]byte
	lastHS    time.Time // когда в последний раз начинали рукопожатие

	lastRecv, lastSend atomic.Int64 // unix-наносекунды

	// защита от потерь
	fmu      sync.Mutex
	enc      fecEnc
	dec      *fecDec
	lossPct  float64   // что нам сообщил собеседник о потерях наших пакетов
	lossOK   time.Time // когда потери последний раз были заметны
	fecFixed bool

	txBytes, rxBytes atomic.Uint64
	txPk, rxPk       atomic.Uint64
	recovered        atomic.Uint64
}

type Engine struct {
	cfg     Config
	static  noise.DHKey
	myObf   [3][32]byte // ключ упаковки рукопожатий по версиям протокола (индекс — версия)
	dev     Device
	conn    *net.UDPConn
	pc      batchIO
	mu      sync.RWMutex
	peers   map[[32]byte]*peer
	byAddr  map[netip.AddrPort]*peer
	byIdx   map[uint32]*session
	closed  atomic.Bool
	gso     atomic.Bool
	wg      sync.WaitGroup
	pool    sync.Pool
	fecK    int
	fecM    int
	fecAuto bool
	hsLimit *hsLimiter    // ограничитель рукопожатий (сервер)
	hsDrop  atomic.Uint64 // сколько рукопожатий отброшено ограничителем
	Log     func(format string, a ...any)
}

func NewEngine(cfg Config, dev Device) (*Engine, error) {
	if len(cfg.PrivateKey) != 32 {
		return nil, errors.New("нет закрытого ключа")
	}
	pub, err := pubFromPriv(cfg.PrivateKey)
	if err != nil {
		return nil, err
	}
	e := &Engine{
		cfg:     cfg,
		static:  noise.DHKey{Private: cfg.PrivateKey, Public: pub},
		dev:     dev,
		peers:   map[[32]byte]*peer{},
		byAddr:  map[netip.AddrPort]*peer{},
		byIdx:   map[uint32]*session{},
		hsLimit: newHSLimiter(),
		Log:     func(string, ...any) {},
	}
	e.myObf[protoV1] = obfKeyFor(pub, protoV1)
	e.myObf[protoV2] = obfKeyFor(pub, protoV2)
	e.pool.New = func() any { b := make([]byte, bufSize); return &b }
	switch cfg.FEC {
	case "", "auto":
		e.fecAuto = true
	case "off":
	default:
		var k, m int
		if _, err := fmt.Sscanf(cfg.FEC, "%d+%d", &k, &m); err != nil || k < 1 || m < 1 || k+m > 64 {
			return nil, fmt.Errorf("FEC: ожидается off, auto или K+M (например 10+2), а не %q", cfg.FEC)
		}
		e.fecK, e.fecM = k, m
	}
	for _, pc := range cfg.Peers {
		if len(pc.PublicKey) != 32 {
			return nil, errors.New("ключ собеседника должен быть 32 байта")
		}
		p := &peer{e: e, allowed: pc.Allowed, keepalive: pc.Keepalive, dec: newFecDec()}
		copy(p.pub[:], pc.PublicKey)
		p.ver = protoV1
		switch cfg.Proto {
		case 0, protoV1:
		case protoV2:
			p.ver = protoV2
		default:
			return nil, errors.New("Proto: поддерживаются версии 1 и 2")
		}
		p.obf = obfKeyFor(pc.PublicKey, p.ver)
		if pc.Endpoint.IsValid() {
			if !pc.Endpoint.Addr().Unmap().Is4() {
				return nil, errors.New("пока поддерживается только IPv4-адрес сервера (внутри туннеля IPv6 уже можно)")
			}
			p.fixedEP = true
			p.setEndpoint(pc.Endpoint)
		}
		p.fecFixed = e.fecK > 0
		if p.fecFixed {
			p.enc.setLevel(e.fecK, e.fecM)
		}
		e.peers[p.pub] = p
	}
	return e, nil
}

func (p *peer) setEndpoint(ap netip.AddrPort) {
	ua := net.UDPAddrFromAddrPort(ap)
	old := p.endpoint.Swap(ua)
	p.e.mu.Lock()
	if old != nil && old.AddrPort() != ap {
		delete(p.e.byAddr, old.AddrPort()) // старые адреса не копим
	}
	p.e.byAddr[ap] = p
	p.e.mu.Unlock()
}

// Start открывает сокет и запускает рабочие потоки.
func (e *Engine) Start() error {
	conn, err := net.ListenUDP("udp4", &net.UDPAddr{Port: e.cfg.ListenPort})
	if err != nil {
		return err
	}
	_ = conn.SetReadBuffer(4 << 20)
	_ = conn.SetWriteBuffer(4 << 20)
	e.conn = conn
	e.pc = newBatchIO(conn)
	if _, ok := e.pc.(plainIO); !ok {
		e.gso.Store(probeGSO(conn))
	}
	e.wg.Add(3)
	go func() { defer e.wg.Done(); e.rxLoop() }()
	go func() { defer e.wg.Done(); e.tunLoop() }()
	go func() { defer e.wg.Done(); e.timerLoop() }()
	return nil
}

func (e *Engine) LocalPort() int { return e.conn.LocalAddr().(*net.UDPAddr).Port }

func (e *Engine) Close() {
	if e.closed.Swap(true) {
		return
	}
	for _, p := range e.snapshotPeers() {
		if s := p.cur.Load(); s != nil {
			e.sendControl(p, s, frClose, nil)
		}
	}
	e.conn.Close()
	e.dev.Close()
	e.wg.Wait()
}

func (e *Engine) snapshotPeers() []*peer {
	e.mu.RLock()
	defer e.mu.RUnlock()
	out := make([]*peer, 0, len(e.peers))
	for _, p := range e.peers {
		out = append(out, p)
	}
	return out
}

// ---------- маршрутизация по ключам (какому собеседнику адресован IP-пакет) ----------

func ipDst(pkt []byte) (netip.Addr, bool) {
	if len(pkt) < 20 {
		return netip.Addr{}, false
	}
	switch pkt[0] >> 4 {
	case 4:
		return netip.AddrFrom4([4]byte(pkt[16:20])), true
	case 6:
		if len(pkt) < 40 {
			return netip.Addr{}, false
		}
		return netip.AddrFrom16([16]byte(pkt[24:40])), true
	}
	return netip.Addr{}, false
}

func ipSrc(pkt []byte) (netip.Addr, bool) {
	if len(pkt) < 20 {
		return netip.Addr{}, false
	}
	switch pkt[0] >> 4 {
	case 4:
		return netip.AddrFrom4([4]byte(pkt[12:16])), true
	case 6:
		if len(pkt) < 40 {
			return netip.Addr{}, false
		}
		return netip.AddrFrom16([16]byte(pkt[8:24])), true
	}
	return netip.Addr{}, false
}

func (p *peer) allows(a netip.Addr) (int, bool) {
	best := -1
	for _, pf := range p.allowed {
		if pf.Contains(a) && pf.Bits() > best {
			best = pf.Bits()
		}
	}
	return best, best >= 0
}

func (e *Engine) route(dst netip.Addr) *peer {
	var best *peer
	bestBits := -1
	e.mu.RLock()
	defer e.mu.RUnlock()
	for _, p := range e.peers {
		if b, ok := p.allows(dst); ok && b > bestBits {
			best, bestBits = p, b
		}
	}
	return best
}

// ---------- отправка ----------

type txBatch struct {
	bufs  [][]byte // буферы, в которые читаем из TUN (только у конвейера)
	sizes []int
	msgs  []ipv4.Message
	pool  []*[]byte // добавочные буферы (проверочные куски), вернём после отправки

	gmsgs []ipv4.Message // склеенные посылки (UDP GSO)
	giov  [][]byte
	goob  []byte
}

func (e *Engine) flushTx(b *txBatch) {
	if len(b.msgs) == 0 {
		return
	}
	if e.cfg.DebugDropTx > 0 {
		kept := b.msgs[:0]
		for _, m := range b.msgs {
			if randFloat() >= e.cfg.DebugDropTx {
				kept = append(kept, m)
			}
		}
		b.msgs = kept
	}
	msgs := b.msgs
	if e.gso.Load() {
		msgs = b.coalesce()
	}
	for len(msgs) > 0 {
		n, err := e.pc.WriteBatch(msgs, 0)
		if err != nil || n <= 0 {
			if err != nil && e.gso.Load() && len(msgs[0].OOB) > 0 {
				// сетевая карта/ядро не принимают склеенные посылки — отключаем и шлём по одному
				e.gso.Store(false)
				e.Log("UDP GSO не работает (%v) — отключено", err)
				msgs = b.msgs
				continue
			}
			break
		}
		msgs = msgs[n:]
	}
	b.msgs = b.msgs[:0]
	for _, bp := range b.pool {
		e.pool.Put(bp)
	}
	b.pool = b.pool[:0]
}

func (b *txBatch) add(buf []byte, n int, to *net.UDPAddr) {
	b.msgs = append(b.msgs, ipv4.Message{Buffers: [][]byte{buf[:n]}, Addr: to})
}

// sendFrame: кадр уже лежит в buf[hdrLen:hdrLen+plen].
func (e *Engine) emit(p *peer, s *session, b *txBatch, buf []byte, plen int) {
	to := p.endpoint.Load()
	if to == nil {
		return
	}
	n := s.seal(buf, plen)
	p.txBytes.Add(uint64(n))
	p.txPk.Add(1)
	p.lastSend.Store(time.Now().UnixNano())
	b.add(buf, n, to)
}

func (e *Engine) sendControl(p *peer, s *session, typ byte, body []byte) {
	bp := e.pool.Get().(*[]byte)
	buf := *bp
	buf[hdrLen] = typ
	pad := 0
	if len(body)+2 < minPlain {
		pad = minPlain - 2 - len(body)
	}
	buf[hdrLen+1] = byte(pad)
	copy(buf[hdrLen+2:], body)
	for i := 0; i < pad; i++ {
		buf[hdrLen+2+len(body)+i] = 0
	}
	var b txBatch
	b.pool = append(b.pool, bp)
	e.emit(p, s, &b, buf, 2+len(body)+pad)
	e.flushTx(&b)
}

func (e *Engine) sendParity(p *peer, s *session, b *txBatch, par *fecParity) {
	for i, sh := range par.shards {
		bp := e.pool.Get().(*[]byte)
		buf := *bp
		if hdrLen+2+7+len(sh) > len(buf) {
			e.pool.Put(bp)
			continue
		}
		buf[hdrLen] = frFecPar
		buf[hdrLen+1] = 0
		o := hdrLen + 2
		binary.BigEndian.PutUint32(buf[o:], par.gid)
		buf[o+4], buf[o+5], buf[o+6] = byte(par.k), byte(par.m), byte(i)
		copy(buf[o+7:], sh)
		b.pool = append(b.pool, bp)
		e.emit(p, s, b, buf, 2+7+len(sh))
	}
}

// Отправка — конвейер из двух стадий на разных потоках: «читаю из TUN и шифрую» и «отдаю в сеть».
// Работа сетевого стека ядра (она бывает тяжёлой) не мешает читать следующую порцию.
const pipeDepth = 8

func (e *Engine) tunLoop() {
	batch := e.dev.BatchSize()
	if batch < 1 {
		batch = 1
	}
	free := make(chan *txBatch, pipeDepth)
	send := make(chan *txBatch, pipeDepth)
	for i := 0; i < pipeDepth; i++ {
		b := &txBatch{bufs: make([][]byte, batch), sizes: make([]int, batch)}
		for j := range b.bufs {
			b.bufs[j] = make([]byte, bufSize)
		}
		free <- b
	}
	done := make(chan struct{})
	go func() { // стадия 2: отправка
		defer close(done)
		for b := range send {
			e.flushTx(b)
			free <- b
		}
	}()
	defer func() { close(send); <-done }()
	for !e.closed.Load() {
		var tx *txBatch
		select {
		case tx = <-free:
		case <-time.After(200 * time.Millisecond):
			continue
		}
		n, err := e.dev.Read(tx.bufs, tx.sizes, bodyOff)
		if err != nil {
			free <- tx
			if e.closed.Load() {
				return
			}
			e.Log("чтение из TUN: %v", err)
			time.Sleep(50 * time.Millisecond)
			continue
		}
		for i := 0; i < n; i++ {
			sz := tx.sizes[i]
			if sz <= 0 || bodyOff+sz+8+tagLen > bufSize {
				continue
			}
			pkt := tx.bufs[i][bodyOff : bodyOff+sz]
			dst, ok := ipDst(pkt)
			if !ok {
				continue
			}
			p := e.route(dst)
			if p == nil {
				continue
			}
			s := p.cur.Load()
			if s == nil {
				e.kickHandshake(p)
				continue
			}
			e.sendIP(p, s, tx, tx.bufs[i], sz)
		}
		send <- tx
	}
}

// sendIP: IP-пакет лежит в buf[bodyOff:bodyOff+sz].
func (e *Engine) sendIP(p *peer, s *session, tx *txBatch, buf []byte, sz int) {
	p.fmu.Lock()
	fecOn := p.enc.k > 0 && p.enc.m > 0
	var par *fecParity
	var gid uint32
	var idx int
	if fecOn {
		gid, idx, par = p.enc.add(buf[bodyOff : bodyOff+sz])
	}
	p.fmu.Unlock()

	pad := 0
	if e.cfg.PadMax > 0 {
		pad = randByte() % (e.cfg.PadMax + 1)
	}
	end := bodyOff + sz
	if fecOn {
		if end+5+pad+tagLen > bufSize {
			pad = 0
		}
		buf[hdrLen] = frFecDat
		binary.BigEndian.PutUint32(buf[end:], gid)
		buf[end+4] = byte(idx)
		end += 5
	} else {
		if end+pad+tagLen > bufSize {
			pad = 0
		}
		buf[hdrLen] = frData
	}
	buf[hdrLen+1] = byte(pad)
	for i := 0; i < pad; i++ {
		buf[end+i] = 0
	}
	e.emit(p, s, tx, buf, end+pad-hdrLen)
	if par != nil {
		e.sendParity(p, s, tx, par)
	}
}

// ---------- приём ----------

// Приём — три стадии: «читаю из сети» → «расшифровываю» → «пишу в TUN».
type rxBatch struct {
	msgs  []ipv4.Message
	bufs  [][]byte
	n     int
	out   [][]byte
	extra []*[]byte
}

func (e *Engine) rxLoop() {
	const N = 64
	free := make(chan *rxBatch, pipeDepth)
	dec := make(chan *rxBatch, pipeDepth)
	wr := make(chan *rxBatch, pipeDepth)
	for i := 0; i < pipeDepth; i++ {
		b := &rxBatch{msgs: make([]ipv4.Message, N), bufs: make([][]byte, N), out: make([][]byte, 0, N+16)}
		for j := range b.msgs {
			b.bufs[j] = make([]byte, bufSize)
			b.msgs[j].Buffers = [][]byte{b.bufs[j]}
		}
		free <- b
	}
	var wg sync.WaitGroup
	wg.Add(2)
	go func() { // стадия 2: расшифровка (здесь же рукопожатия и служебные кадры — всё состояние меняется в одном потоке)
		defer wg.Done()
		defer close(wr)
		for b := range dec {
			b.out = b.out[:0]
			for i := 0; i < b.n; i++ {
				ua, _ := b.msgs[i].Addr.(*net.UDPAddr)
				if ua == nil {
					continue
				}
				src := ua.AddrPort()
				src = netip.AddrPortFrom(src.Addr().Unmap(), src.Port())
				pk := b.bufs[i][:b.msgs[i].N]
				if ip := e.handlePacket(pk, b.bufs[i], src, ua, &b.extra); ip != nil {
					b.out = append(b.out, ip...)
				}
			}
			wr <- b
		}
	}()
	go func() { // стадия 3: запись в TUN
		defer wg.Done()
		for b := range wr {
			if len(b.out) > 0 {
				if _, err := e.dev.Write(b.out, bodyOff); err != nil && !e.closed.Load() {
					e.Log("запись в TUN: %v", err)
				}
			}
			for _, bp := range b.extra {
				e.pool.Put(bp)
			}
			b.extra = b.extra[:0]
			for i := range b.msgs {
				b.msgs[i].Buffers[0] = b.bufs[i][:bufSize]
			}
			free <- b
		}
	}()
	defer func() { close(dec); wg.Wait() }()
	for !e.closed.Load() {
		var b *rxBatch
		select {
		case b = <-free:
		case <-time.After(200 * time.Millisecond):
			continue
		}
		n, err := e.pc.ReadBatch(b.msgs, 0)
		if err != nil {
			free <- b
			if e.closed.Load() {
				return
			}
			e.Log("чтение из сети: %v", err)
			time.Sleep(20 * time.Millisecond)
			continue
		}
		b.n = n
		dec <- b
	}
}

// handlePacket разбирает один принятый пакет. Возвращает пакеты, которые надо записать в TUN
// (каждый — срез буфера, IP-пакет начинается с bodyOff).
func (e *Engine) handlePacket(pk, full []byte, src netip.AddrPort, ua *net.UDPAddr, extra *[]*[]byte) [][]byte {
	if s, hdr := e.findSession(pk, src); s != nil {
		return e.handleData(s, pk, full, hdr, src, ua, extra)
	}
	// не данные — рукопожатие
	e.handleHandshake(pk, src, ua)
	return nil
}

func (e *Engine) findSession(pk []byte, src netip.AddrPort) (*session, [hdrLen]byte) {
	e.mu.RLock()
	p := e.byAddr[src]
	e.mu.RUnlock()
	if p != nil {
		for _, s := range [2]*session{p.cur.Load(), p.prev.Load()} {
			if s != nil {
				if hdr, ok := s.peekIndex(pk); ok {
					return s, hdr
				}
			}
		}
	}
	// адрес незнакомый (собеседник сменил сеть) — пробуем все сеансы
	for _, q := range e.snapshotPeers() {
		if q == p {
			continue
		}
		for _, s := range [2]*session{q.cur.Load(), q.prev.Load()} {
			if s != nil {
				if hdr, ok := s.peekIndex(pk); ok {
					return s, hdr
				}
			}
		}
	}
	return nil, [hdrLen]byte{}
}

func (e *Engine) handleData(s *session, pk, full []byte, hdr [hdrLen]byte, src netip.AddrPort, ua *net.UDPAddr, extra *[]*[]byte) [][]byte {
	p := s.peer
	plain, _, err := s.open(pk, hdr)
	if err != nil {
		return nil
	}
	now := time.Now()
	p.lastRecv.Store(now.UnixNano())
	p.rxBytes.Add(uint64(len(pk)))
	p.rxPk.Add(1)
	// собеседник мог сменить адрес
	if cur := p.endpoint.Load(); cur == nil || cur.AddrPort() != src {
		p.setEndpoint(src)
	}
	if len(plain) < 2 {
		return nil
	}
	typ, pad := plain[0], int(plain[1])
	end := len(plain) - pad
	if end < 2 {
		return nil
	}
	switch typ {
	case frData:
		return e.deliver(p, full, 2, end-2, hdrLen)
	case frFecDat:
		if end < 2+5 {
			return nil
		}
		meta := plain[end-5 : end]
		gid := binary.BigEndian.Uint32(meta)
		idx := int(meta[4])
		pktLen := end - 5 - 2
		p.fmu.Lock()
		fresh := p.dec.onData(gid, idx, plain[2:2+pktLen])
		p.fmu.Unlock()
		if !fresh {
			return nil
		}
		return e.deliver(p, full, 2, pktLen, hdrLen)
	case frFecPar:
		if end < 2+7+1 {
			return nil
		}
		b := plain[2:end]
		gid := binary.BigEndian.Uint32(b)
		k, m, pi := int(b[4]), int(b[5]), int(b[6])
		p.fmu.Lock()
		rec := p.dec.onParity(gid, k, m, pi, b[7:])
		p.fmu.Unlock()
		var out [][]byte
		for _, r := range rec {
			bp := e.pool.Get().(*[]byte)
			buf := *bp
			if bodyOff+len(r) > len(buf) {
				e.pool.Put(bp)
				continue
			}
			copy(buf[bodyOff:], r)
			if src2, ok := ipSrc(r); ok {
				if _, ok := p.allows(src2); !ok {
					e.pool.Put(bp)
					continue
				}
			}
			*extra = append(*extra, bp)
			p.recovered.Add(1)
			out = append(out, buf[:bodyOff+len(r)])
		}
		return out
	case frReport:
		if end >= 2+8 {
			recv := binary.BigEndian.Uint32(plain[2:])
			exp := binary.BigEndian.Uint32(plain[6:])
			e.onReport(p, recv, exp)
		}
	case frClose:
		p.cur.Store(nil)
		p.prev.Store(nil)
	}
	return nil
}

// deliver проверяет «кто имеет право слать с такого адреса» и отдаёт срез буфера для записи в TUN.
// Кадр лежит в full[hdrLen:], IP-пакет — с full[bodyOff:].
func (e *Engine) deliver(p *peer, full []byte, off, n, _ int) [][]byte {
	if n <= 0 {
		return nil
	}
	pkt := full[bodyOff : bodyOff+n]
	src, ok := ipSrc(pkt)
	if !ok {
		return nil
	}
	if _, ok := p.allows(src); !ok {
		return nil
	}
	return [][]byte{full[:bodyOff+n]}
}

func (e *Engine) onReport(p *peer, recv, exp uint32) {
	if exp < 20 || e.fecK > 0 || !e.fecAuto {
		return
	}
	loss := 0.0
	if recv < exp {
		loss = 100 * float64(exp-recv) / float64(exp)
	}
	p.fmu.Lock()
	defer p.fmu.Unlock()
	p.lossPct = 0.5*p.lossPct + 0.5*loss
	now := time.Now()
	k, m := fecLevelFor(p.lossPct)
	if k > 0 {
		p.lossOK = now
	} else if now.Sub(p.lossOK) < 15*time.Second {
		return // держим защиту ещё 15 секунд после затишья
	}
	if par := p.enc.setLevel(k, m); par != nil {
		if s := p.cur.Load(); s != nil {
			var b txBatch
			e.sendParity(p, s, &b, par)
			e.flushTx(&b)
		}
	}
}

// ---------- рукопожатие ----------

func (e *Engine) kickHandshake(p *peer) {
	if !p.fixedEP {
		return // рукопожатие начинает только сторона, которая знает адрес собеседника из настроек
	}
	p.mu.Lock()
	if time.Since(p.lastHS) < 3*time.Second || !p.endpoint.Load().AddrPort().IsValid() {
		p.mu.Unlock()
		return
	}
	p.lastHS = time.Now()
	idx := randU32()
	pend, msg, err := buildInit(e.static, p.pub[:], idx, p.ver)
	if err != nil {
		p.mu.Unlock()
		return
	}
	p.pending = pend
	to := p.endpoint.Load()
	p.mu.Unlock()
	pkt := sealHandshake(&p.obf, msg)
	_, _ = e.conn.WriteToUDP(pkt, to)
	e.Log("рукопожатие → %v", to)
}

func (e *Engine) handleHandshake(pk []byte, src netip.AddrPort, ua *net.UDPAddr) {
	// 1) ответ на наш init (мы клиент)
	for _, p := range e.snapshotPeers() {
		p.mu.Lock()
		pend := p.pending
		p.mu.Unlock()
		if pend == nil {
			continue
		}
		msg, err := openHandshake(&p.obf, pk)
		if err != nil {
			continue
		}
		srvIdx, send, recv, shp, rhp, err := parseResponse(pend, msg)
		if err != nil {
			continue
		}
		s := newSession(pend.ver, pend.idx, srvIdx, send, recv, shp, rhp, p)
		e.install(p, s)
		p.mu.Lock()
		p.pending = nil
		p.mu.Unlock()
		if cur := p.endpoint.Load(); cur == nil || cur.AddrPort() != src {
			p.setEndpoint(src)
		}
		p.lastRecv.Store(time.Now().UnixNano())
		e.sendControl(p, s, frKeep, nil) // подтверждаем серверу, что ключи у нас
		e.Log("сеанс установлен с %v", src)
		return
	}
	// 2) init от клиента (мы сервер)
	// сервер понимает обе версии: смотрим, чьим ключом упаковано рукопожатие
	var msg []byte
	var hver int
	for _, v := range [2]int{protoV2, protoV1} {
		if m, err := openHandshake(&e.myObf[v], pk); err == nil {
			msg, hver = m, v
			break
		}
	}
	if hver == 0 {
		return // чужой/битый пакет: молчим, ответа не получит никто
	}
	// разбор init стоит двух умножений на кривой: сначала ограничитель (см. ratelimit.go)
	if !e.hsLimit.allow(src.Addr(), time.Now()) {
		e.hsDrop.Add(1)
		return
	}
	info, err := parseInit(e.static, msg, hver)
	if err != nil {
		return
	}
	var key [32]byte
	copy(key[:], info.clientPub)
	e.mu.RLock()
	p := e.peers[key]
	e.mu.RUnlock()
	if p == nil {
		return // неизвестный ключ
	}
	p.mu.Lock()
	if !tsLess(p.lastTS, info.ts) {
		p.mu.Unlock()
		return // повтор старого init
	}
	p.lastTS = info.ts
	p.mu.Unlock()
	srvIdx := randU32()
	rmsg, send, recv, shp, rhp, err := buildResponse(info, srvIdx)
	if err != nil {
		return
	}
	s := newSession(hver, srvIdx, info.clientIdx, send, recv, shp, rhp, p)
	e.install(p, s)
	p.setEndpoint(src)
	pkt := sealHandshake(&e.myObf[hver], rmsg)
	_, _ = e.conn.WriteToUDP(pkt, ua)
	e.Log("новый сеанс от %v", src)
}

func (e *Engine) install(p *peer, s *session) {
	e.mu.Lock()
	if old := p.prev.Load(); old != nil {
		delete(e.byIdx, old.id)
	}
	e.byIdx[s.id] = s
	e.mu.Unlock()
	p.prev.Store(p.cur.Load())
	p.cur.Store(s)
}

// NetworkChanged: сеть под нами сменилась (на Windows поставлен новый маршрут к серверу). Сразу даём серверу знать новый адрес
// (первый же подлинный пакет обновит его эндпоинт) и через пару секунд проверяем, что от сервера что-то пришло; если нет — новое рукопожатие.
func (e *Engine) NetworkChanged() {
	now := time.Now()
	for _, p := range e.snapshotPeers() {
		if !p.fixedEP {
			continue
		}
		if s := p.cur.Load(); s != nil {
			e.sendControl(p, s, frKeep, nil)
		}
		p := p
		time.AfterFunc(3*time.Second, func() {
			if e.closed.Load() || time.Unix(0, p.lastRecv.Load()).After(now) {
				return
			}
			p.mu.Lock()
			p.lastHS = time.Time{}
			p.mu.Unlock()
			e.kickHandshake(p)
		})
	}
}

// ---------- таймеры ----------

func (e *Engine) timerLoop() {
	t := time.NewTicker(time.Second)
	defer t.Stop()
	flush := time.NewTicker(4 * time.Millisecond)
	defer flush.Stop()
	for !e.closed.Load() {
		select {
		case <-t.C:
			e.tick()
		case <-flush.C:
			e.fecFlush()
		}
		if e.closed.Load() {
			return
		}
	}
}

// Неполные группы закрываем по таймеру, чтобы проверочные куски не зависали при редком трафике.
func (e *Engine) fecFlush() {
	for _, p := range e.snapshotPeers() {
		p.fmu.Lock()
		var par *fecParity
		if p.enc.k > 0 && len(p.enc.shards) > 0 && time.Since(p.enc.lastAdd) > 6*time.Millisecond {
			par = p.enc.flush()
		}
		p.fmu.Unlock()
		if par != nil {
			if s := p.cur.Load(); s != nil {
				var b txBatch
				e.sendParity(p, s, &b, par)
				e.flushTx(&b)
			}
		}
	}
}

func (e *Engine) tick() {
	now := time.Now()
	for _, p := range e.snapshotPeers() {
		s := p.cur.Load()
		isClient := p.endpoint.Load() != nil && p.configuredEndpoint()
		if s == nil {
			if isClient {
				e.kickHandshake(p)
			}
			continue
		}
		if old := p.prev.Load(); old != nil && now.Sub(s.created) > 15*time.Second {
			p.prev.Store(nil)
			e.mu.Lock()
			delete(e.byIdx, old.id)
			e.mu.Unlock()
		}
		// плановая смена ключей и «связь пропала»
		if isClient {
			stale := now.Sub(time.Unix(0, p.lastRecv.Load())) > 30*time.Second && p.lastSend.Load() > p.lastRecv.Load()
			if now.Sub(s.created) > 120*time.Second || stale {
				e.kickHandshake(p)
			}
		}
		if p.keepalive > 0 && now.Sub(time.Unix(0, p.lastSend.Load())) >= p.keepalive {
			e.sendControl(p, s, frKeep, nil)
		}
		// отчёт о потерях
		s.mu.Lock()
		got, exp := s.rxCnt, uint32(s.rxMax-s.rxRef)
		if s.rxMax >= s.rxRef {
			s.rxRef, s.rxCnt = s.rxMax, 0
		}
		s.mu.Unlock()
		if exp >= 20 {
			var body [8]byte
			binary.BigEndian.PutUint32(body[0:], got)
			binary.BigEndian.PutUint32(body[4:], exp)
			e.sendControl(p, s, frReport, body[:])
		}
		p.fmu.Lock()
		p.dec.expire(now)
		p.fmu.Unlock()
	}
}

// Клиентом считается тот, у кого эндпоинт задан в настройках; серверу адрес приходит сам.
func (p *peer) configuredEndpoint() bool { return p.fixedEP }

func randFloat() float64 { return float64(randU32()) / 4294967296.0 }
