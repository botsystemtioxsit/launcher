package main

import (
	"encoding/binary"
	"errors"
	"math/rand"
	"net"
	"net/netip"
	"sync"
	"sync/atomic"
	"testing"
	"time"
)

// ---- подделка сетевой карты ----

type fakeDev struct {
	in     chan []byte
	out    chan []byte
	closed chan struct{}
	once   sync.Once
}

func newFakeDev() *fakeDev {
	return &fakeDev{in: make(chan []byte, 4096), out: make(chan []byte, 65536), closed: make(chan struct{})}
}

func (d *fakeDev) Read(bufs [][]byte, sizes []int, offset int) (int, error) {
	select {
	case p := <-d.in:
		copy(bufs[0][offset:], p)
		sizes[0] = len(p)
		return 1, nil
	case <-d.closed:
		return 0, errors.New("closed")
	}
}
func (d *fakeDev) Write(bufs [][]byte, offset int) (int, error) {
	for _, b := range bufs {
		select {
		case d.out <- append([]byte(nil), b[offset:]...):
		default:
		}
	}
	return len(bufs), nil
}
func (d *fakeDev) BatchSize() int { return 1 }
func (d *fakeDev) Close() error   { d.once.Do(func() { close(d.closed) }); return nil }

func mkIP(src, dst [4]byte, seq uint32, size int) []byte {
	if size < 28 {
		size = 28
	}
	p := make([]byte, size)
	p[0] = 0x45
	binary.BigEndian.PutUint16(p[2:], uint16(size))
	p[8], p[9] = 64, 17
	copy(p[12:], src[:])
	copy(p[16:], dst[:])
	binary.BigEndian.PutUint32(p[20:], seq)
	return p
}

var (
	cliIP = [4]byte{10, 77, 0, 2}
	srvIP = [4]byte{10, 77, 0, 1}
)

// ---- прокладка с потерями ----

type relay struct {
	conn    *net.UDPConn
	srv     *net.UDPAddr
	cli     atomic.Pointer[net.UDPAddr]
	drop    float64
	delay   time.Duration
	seen    atomic.Int64
	dropped atomic.Int64
	capture chan []byte
	stop    chan struct{}
}

func newRelay(t *testing.T, srvPort int, drop float64) *relay {
	c, err := net.ListenUDP("udp4", &net.UDPAddr{IP: net.IPv4(127, 0, 0, 1)})
	if err != nil {
		t.Fatal(err)
	}
	r := &relay{conn: c, srv: &net.UDPAddr{IP: net.IPv4(127, 0, 0, 1), Port: srvPort}, drop: drop, capture: make(chan []byte, 1024), stop: make(chan struct{})}
	srvSide, _ := net.ListenUDP("udp4", &net.UDPAddr{IP: net.IPv4(127, 0, 0, 1)})
	rng := rand.New(rand.NewSource(7))
	var rmu sync.Mutex
	lost := func() bool {
		rmu.Lock()
		defer rmu.Unlock()
		return rng.Float64() < r.drop
	}
	fwd := func(dst *net.UDPConn, to *net.UDPAddr, b []byte) {
		if r.delay > 0 {
			time.AfterFunc(r.delay, func() { dst.WriteToUDP(b, to) })
		} else {
			dst.WriteToUDP(b, to)
		}
	}
	go func() { // клиент → сервер
		buf := make([]byte, 4096)
		for {
			n, a, err := c.ReadFromUDP(buf)
			if err != nil {
				return
			}
			r.cli.Store(a)
			r.seen.Add(1)
			b := append([]byte(nil), buf[:n]...)
			select {
			case r.capture <- b:
			default:
			}
			if lost() {
				r.dropped.Add(1)
				continue
			}
			fwd(srvSide, r.srv, b)
		}
	}()
	go func() { // сервер → клиент
		buf := make([]byte, 4096)
		for {
			n, _, err := srvSide.ReadFromUDP(buf)
			if err != nil {
				return
			}
			a := r.cli.Load()
			if a == nil || lost() {
				r.dropped.Add(1)
				continue
			}
			fwd(c, a, append([]byte(nil), buf[:n]...))
		}
	}()
	return r
}
func (r *relay) addr() netip.AddrPort { return r.conn.LocalAddr().(*net.UDPAddr).AddrPort() }
func (r *relay) close()               { r.conn.Close() }

// ---- пара узлов ----

type pair struct {
	srv, cli       *Engine
	srvDev, cliDev *fakeDev
	rel            *relay
}

func newPair(t *testing.T, fec string, drop float64, mutate func(srv, cli *Config)) *pair {
	t.Helper()
	sk, _ := newKeypair()
	ck, _ := newKeypair()
	sc := Config{PrivateKey: sk.Private, FEC: fec, Peers: []PeerConfig{{PublicKey: ck.Public, Allowed: []netip.Prefix{netip.MustParsePrefix("10.77.0.2/32")}}}}
	cc := Config{PrivateKey: ck.Private, FEC: fec, Peers: []PeerConfig{{PublicKey: sk.Public, Allowed: []netip.Prefix{netip.MustParsePrefix("0.0.0.0/0")}, Keepalive: 0}}}
	sdev, cdev := newFakeDev(), newFakeDev()
	if mutate != nil {
		mutate(&sc, &cc)
	}
	srv, err := NewEngine(sc, sdev)
	if err != nil {
		t.Fatal(err)
	}
	if err := srv.Start(); err != nil {
		t.Fatal(err)
	}
	rel := newRelay(t, srv.LocalPort(), drop)
	cc.Peers[0].Endpoint = rel.addr()
	cli, err := NewEngine(cc, cdev)
	if err != nil {
		t.Fatal(err)
	}
	if err := cli.Start(); err != nil {
		t.Fatal(err)
	}
	p := &pair{srv: srv, cli: cli, srvDev: sdev, cliDev: cdev, rel: rel}
	t.Cleanup(func() { cli.Close(); srv.Close(); rel.close() })
	return p
}

func recvN(d *fakeDev, n int, wait time.Duration) [][]byte {
	var out [][]byte
	deadline := time.After(wait)
	for len(out) < n {
		select {
		case p := <-d.out:
			out = append(out, p)
		case <-deadline:
			return out
		}
	}
	return out
}

func TestReplayWindow(t *testing.T) {
	var w replayWin
	for _, c := range []uint64{0, 1, 5, 3} {
		if !w.ok(c) {
			t.Fatalf("%d должен приниматься", c)
		}
		w.mark(c)
	}
	for _, c := range []uint64{0, 1, 3, 5} {
		if w.ok(c) {
			t.Fatalf("%d — повтор, должен отбрасываться", c)
		}
	}
	w.mark(5000)
	if w.ok(100) {
		t.Fatal("слишком старый пакет должен отбрасываться")
	}
	if !w.ok(4999) {
		t.Fatal("4999 внутри окна и ещё не виден")
	}
}

func TestHandshakeAndData(t *testing.T) {
	p := newPair(t, "off", 0, nil)
	// первый пакет только запускает рукопожатие (как и в WireGuard) — шлём, пока не пойдёт
	deadline := time.Now().Add(5 * time.Second)
	for p.cli.peerOnly().cur.Load() == nil && time.Now().Before(deadline) {
		p.cliDev.in <- mkIP(cliIP, srvIP, 0, 100)
		time.Sleep(50 * time.Millisecond)
	}
	if p.cli.peerOnly().cur.Load() == nil {
		t.Fatal("сеанс не установился")
	}
	for i := uint32(1); i <= 50; i++ {
		p.cliDev.in <- mkIP(cliIP, srvIP, i, 200)
	}
	got := recvN(p.srvDev, 50, 3*time.Second)
	if len(got) < 50 {
		t.Fatalf("на сервер дошло %d из 50", len(got))
	}
	// обратно
	for i := uint32(1); i <= 50; i++ {
		p.srvDev.in <- mkIP(srvIP, cliIP, i, 300)
	}
	back := recvN(p.cliDev, 50, 3*time.Second)
	if len(back) != 50 {
		t.Fatalf("на клиент дошло %d из 50", len(back))
	}
	if len(back[0]) != 300 {
		t.Fatalf("длина пакета изменилась: %d", len(back[0]))
	}
}

func TestWrongServerKeyGetsNothing(t *testing.T) {
	p := newPair(t, "off", 0, func(srv, cli *Config) {
		other, _ := newKeypair()
		cli.Peers[0].PublicKey = other.Public // клиент «знает» не тот ключ сервера
	})
	for i := 0; i < 10; i++ {
		p.cliDev.in <- mkIP(cliIP, srvIP, uint32(i), 100)
		time.Sleep(100 * time.Millisecond)
	}
	if p.cli.peerOnly().cur.Load() != nil || p.srv.peerOnly().cur.Load() != nil {
		t.Fatal("сеанс не должен был установиться")
	}
	if len(p.srvDev.out) != 0 {
		t.Fatal("ничего не должно дойти")
	}
}

func TestUnknownClientIgnored(t *testing.T) {
	p := newPair(t, "off", 0, func(srv, cli *Config) {
		stranger, _ := newKeypair()
		cli.PrivateKey = stranger.Private // сервер такого ключа не знает
	})
	for i := 0; i < 10; i++ {
		p.cliDev.in <- mkIP(cliIP, srvIP, uint32(i), 100)
		time.Sleep(100 * time.Millisecond)
	}
	if p.cli.peerOnly().cur.Load() != nil {
		t.Fatal("чужой ключ не должен получить сеанс")
	}
}

func TestSourceAddressIsChecked(t *testing.T) {
	p := newPair(t, "off", 0, nil)
	deadline := time.Now().Add(5 * time.Second)
	for p.cli.peerOnly().cur.Load() == nil && time.Now().Before(deadline) {
		p.cliDev.in <- mkIP(cliIP, srvIP, 0, 100)
		time.Sleep(50 * time.Millisecond)
	}
	time.Sleep(200 * time.Millisecond)
	for len(p.srvDev.out) > 0 {
		<-p.srvDev.out
	}
	// клиент подделывает чужой адрес источника — сервер обязан отбросить
	p.cliDev.in <- mkIP([4]byte{10, 77, 0, 99}, srvIP, 1, 100)
	p.cliDev.in <- mkIP(cliIP, srvIP, 2, 100)
	got := recvN(p.srvDev, 2, time.Second)
	if len(got) != 1 || binary.BigEndian.Uint32(got[0][20:]) != 2 {
		t.Fatalf("ожидался только честный пакет, пришло %d", len(got))
	}
}

func TestReplayedPacketDropped(t *testing.T) {
	p := newPair(t, "off", 0, nil)
	deadline := time.Now().Add(5 * time.Second)
	for p.cli.peerOnly().cur.Load() == nil && time.Now().Before(deadline) {
		p.cliDev.in <- mkIP(cliIP, srvIP, 0, 100)
		time.Sleep(50 * time.Millisecond)
	}
	time.Sleep(200 * time.Millisecond)
	for len(p.srvDev.out) > 0 {
		<-p.srvDev.out
	}
	for len(p.rel.capture) > 0 {
		<-p.rel.capture
	}
	p.cliDev.in <- mkIP(cliIP, srvIP, 777, 150)
	if len(recvN(p.srvDev, 1, time.Second)) != 1 {
		t.Fatal("пакет не дошёл")
	}
	var captured []byte
	for len(p.rel.capture) > 0 {
		captured = <-p.rel.capture
	}
	// злоумышленник шлёт тот же пакет ещё раз
	conn, _ := net.DialUDP("udp4", nil, &net.UDPAddr{IP: net.IPv4(127, 0, 0, 1), Port: p.srv.LocalPort()})
	defer conn.Close()
	conn.Write(captured)
	if got := recvN(p.srvDev, 1, 500*time.Millisecond); len(got) != 0 {
		t.Fatal("повторённый пакет был принят")
	}
}

func runLossy(t *testing.T, fec string, drop float64, total int) (delivered int, recovered uint64) {
	p := newPair(t, fec, drop, nil)
	deadline := time.Now().Add(8 * time.Second)
	for p.cli.peerOnly().cur.Load() == nil && time.Now().Before(deadline) {
		p.cliDev.in <- mkIP(cliIP, srvIP, 0, 100)
		time.Sleep(60 * time.Millisecond)
	}
	if p.cli.peerOnly().cur.Load() == nil {
		t.Fatal("сеанс не установился")
	}
	time.Sleep(300 * time.Millisecond)
	for len(p.srvDev.out) > 0 {
		<-p.srvDev.out
	}
	seen := map[uint32]bool{}
	var mu sync.Mutex
	done := make(chan struct{})
	go func() {
		for {
			select {
			case b := <-p.srvDev.out:
				mu.Lock()
				seen[binary.BigEndian.Uint32(b[20:])] = true
				mu.Unlock()
			case <-done:
				return
			}
		}
	}()
	for i := 1; i <= total; i++ {
		p.cliDev.in <- mkIP(cliIP, srvIP, uint32(i), 400)
		if i%20 == 0 {
			time.Sleep(2 * time.Millisecond)
		}
	}
	time.Sleep(800 * time.Millisecond)
	close(done)
	mu.Lock()
	defer mu.Unlock()
	return len(seen), p.srv.peerOnly().recovered.Load()
}

func TestFECRecoversLoss(t *testing.T) {
	const total = 3000
	plain, _ := runLossy(t, "off", 0.05, total)
	fec, rec := runLossy(t, "10+2", 0.05, total)
	t.Logf("потери 5%%: без защиты дошло %d/%d (%.1f%%), с защитой 10+2 — %d/%d (%.2f%%), восстановлено %d",
		plain, total, 100*float64(plain)/total, fec, total, 100*float64(fec)/total, rec)
	if float64(plain)/total > 0.97 {
		t.Fatalf("прокладка почти ничего не теряет (%d/%d) — проверка бессмысленна", plain, total)
	}
	if float64(fec)/total < 0.985 {
		t.Fatalf("с защитой дошло слишком мало: %d/%d", fec, total)
	}
	if rec == 0 {
		t.Fatal("ничего не восстановлено")
	}
}

func TestFECOffCostsNothing(t *testing.T) {
	got, rec := runLossy(t, "10+2", 0, 500)
	if got != 500 || rec != 0 {
		t.Fatalf("без потерь должно дойти всё без восстановлений: %d, %d", got, rec)
	}
}

func TestFECDecoderUnit(t *testing.T) {
	var enc fecEnc
	enc.setLevel(5, 2)
	dec := newFecDec()
	pkts := make([][]byte, 5)
	var par *fecParity
	for i := range pkts {
		pkts[i] = mkIP(cliIP, srvIP, uint32(i), 100+i*37)
		_, _, p := enc.add(pkts[i])
		if p != nil {
			par = p
		}
	}
	if par == nil {
		t.Fatal("проверочные куски не получены")
	}
	// теряем пакеты 1 и 3
	for _, i := range []int{0, 2, 4} {
		dec.onData(par.gid, i, pkts[i])
	}
	if r := dec.onParity(par.gid, par.k, par.m, 0, par.shards[0]); len(r) != 0 {
		t.Fatal("рано: данных не хватает")
	}
	r := dec.onParity(par.gid, par.k, par.m, 1, par.shards[1])
	if len(r) != 2 {
		t.Fatalf("ожидалось 2 восстановленных, получено %d", len(r))
	}
	for _, rp := range r {
		ok := false
		for _, o := range pkts {
			if string(o) == string(rp) {
				ok = true
			}
		}
		if !ok {
			t.Fatal("восстановленный пакет не совпал с исходным")
		}
	}
	if dec.onData(par.gid, 1, pkts[1]) {
		t.Fatal("оригинал после восстановления должен считаться дубликатом")
	}
}

func TestHandshakePacketsLookRandomAndVary(t *testing.T) {
	k, _ := newKeypair()
	ob := obfKeyFor(k.Public)
	sizes := map[int]bool{}
	for i := 0; i < 50; i++ {
		pend, msg, err := buildInit(k, k.Public, uint32(i))
		_ = pend
		if err != nil {
			t.Fatal(err)
		}
		sizes[len(sealHandshake(&ob, msg))] = true
	}
	if len(sizes) < 10 {
		t.Fatalf("размеры рукопожатий почти одинаковые (%d видов)", len(sizes))
	}
}

func (e *Engine) peerOnly() *peer {
	for _, p := range e.snapshotPeers() {
		return p
	}
	return nil
}

func TestEchoDeviceAnswersPing(t *testing.T) {
	req := mkIP([4]byte{10, 77, 0, 2}, [4]byte{10, 77, 0, 1}, 1, 64)
	req[9] = 1  // ICMP
	req[20] = 8 // echo request
	req[22], req[23] = 0, 0
	binary.BigEndian.PutUint16(req[22:], inetChecksum(req[20:]))
	rep := icmpEchoReply(req)
	if rep == nil || rep[20] != 0 {
		t.Fatal("нет ответа на ping")
	}
	if string(rep[12:16]) != string(req[16:20]) || string(rep[16:20]) != string(req[12:16]) {
		t.Fatal("адреса не поменялись местами")
	}
	if inetChecksum(rep[20:]) != 0 || inetChecksum(rep[:20]) != 0 {
		t.Fatal("контрольные суммы ответа неверны")
	}
	if icmpEchoReply(rep) != nil {
		t.Fatal("на ответ отвечать не надо")
	}
}
