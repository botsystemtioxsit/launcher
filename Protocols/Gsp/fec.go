package main

// Защита от потерь (FEC) на коде Рида — Соломона: на каждые k пакетов добавляется m проверочных,
// и до m потерянных из группы восстанавливаются без повторной отправки.
// Это то, чего у WireGuard нет совсем: там потерянный пакет теряется, а внутренний TCP «пугается» и режет скорость.
// Данные уходят сразу (задержки нет), проверочные — следом.

import (
	"encoding/binary"
	"sync"
	"time"

	"github.com/klauspost/reedsolomon"
)

var (
	rsMu    sync.Mutex
	rsCache = map[[2]int]reedsolomon.Encoder{}
)

func rsFor(k, m int) (reedsolomon.Encoder, error) {
	rsMu.Lock()
	defer rsMu.Unlock()
	key := [2]int{k, m}
	if e, ok := rsCache[key]; ok {
		return e, nil
	}
	e, err := reedsolomon.New(k, m)
	if err != nil {
		return nil, err
	}
	rsCache[key] = e
	return e, nil
}

// Выбор силы защиты по доле потерь в сети (в процентах). Цель — чтобы после восстановления
// терялось не больше ~0,05% пакетов: внутреннему TCP хватает и такой малости, чтобы замедлиться в разы.
func fecLevelFor(lossPct float64) (k, m int) {
	switch {
	case lossPct >= 14:
		return 8, 8
	case lossPct >= 7:
		return 8, 6
	case lossPct >= 3:
		return 8, 4
	case lossPct >= 1.2:
		return 8, 3
	case lossPct >= 0.4:
		return 8, 2
	}
	return 0, 0
}

type fecParity struct {
	gid    uint32
	k, m   int
	shards [][]byte // m проверочных кусков
}

// Кодировщик одной стороны. Не потокобезопасен сам по себе — вызывающий держит замок.
type fecEnc struct {
	k, m    int // 0 — выключено
	gid     uint32
	shards  [][]byte // 2 байта длины + IP-пакет
	lastAdd time.Time
}

func (f *fecEnc) setLevel(k, m int) (flushed *fecParity) {
	if f.k != k || f.m != m {
		flushed = f.flush() // старую недособранную группу закрываем со старыми параметрами
		f.k, f.m = k, m
	}
	return
}

// add кладёт пакет в текущую группу; если группа собралась — возвращает проверочные куски.
func (f *fecEnc) add(pkt []byte) (gid uint32, idx int, par *fecParity) {
	gid = f.gid
	idx = len(f.shards)
	sh := make([]byte, 2+len(pkt))
	binary.BigEndian.PutUint16(sh, uint16(len(pkt)))
	copy(sh[2:], pkt)
	f.shards = append(f.shards, sh)
	f.lastAdd = time.Now()
	if len(f.shards) >= f.k {
		par = f.flush()
	}
	return
}

// flush закрывает текущую группу (даже неполную) и считает проверочные куски.
func (f *fecEnc) flush() *fecParity {
	if len(f.shards) == 0 || f.m == 0 {
		f.shards = f.shards[:0]
		return nil
	}
	k := len(f.shards)
	size := 0
	for _, s := range f.shards {
		if len(s) > size {
			size = len(s)
		}
	}
	all := make([][]byte, k+f.m)
	for i, s := range f.shards {
		b := make([]byte, size)
		copy(b, s)
		all[i] = b
	}
	for i := 0; i < f.m; i++ {
		all[k+i] = make([]byte, size)
	}
	enc, err := rsFor(k, f.m)
	if err == nil {
		err = enc.Encode(all)
	}
	gid := f.gid
	f.gid++
	f.shards = f.shards[:0]
	if err != nil {
		return nil
	}
	return &fecParity{gid: gid, k: k, m: f.m, shards: all[k:]}
}

type fgroup struct {
	k, m int
	data map[int][]byte // что пришло (пока группа не закрыта)
	seen map[int]bool   // что уже доставлено — чтобы не отдать пакет дважды (оригинал после восстановления)
	par  map[int][]byte
	done bool
	t    time.Time
}

// Раскодировщик: помнит последние группы.
type fecDec struct {
	groups map[uint32]*fgroup
	order  []uint32
}

func newFecDec() *fecDec { return &fecDec{groups: map[uint32]*fgroup{}} }

func (d *fecDec) group(gid uint32) *fgroup {
	g := d.groups[gid]
	if g == nil {
		g = &fgroup{data: map[int][]byte{}, seen: map[int]bool{}, par: map[int][]byte{}, t: time.Now()}
		d.groups[gid] = g
		d.order = append(d.order, gid)
		for len(d.order) > 256 {
			delete(d.groups, d.order[0])
			d.order = d.order[1:]
		}
	}
	return g
}

// onData запоминает пакет группы. false — это дубликат (уже есть или уже восстановлен), доставлять не надо.
func (d *fecDec) onData(gid uint32, idx int, pkt []byte) bool {
	g := d.group(gid)
	if g.seen[idx] {
		return false
	}
	g.seen[idx] = true
	if !g.done {
		g.data[idx] = append([]byte(nil), pkt...)
	}
	return true
}

// onParity принимает проверочный кусок; если можно восстановить потерянное — возвращает восстановленные пакеты.
func (d *fecDec) onParity(gid uint32, k, m, pidx int, shard []byte) [][]byte {
	if k <= 0 || m <= 0 || pidx < 0 || pidx >= m || k+m > 255 {
		return nil
	}
	g := d.group(gid)
	if g.done {
		return nil
	}
	g.k, g.m = k, m
	g.par[pidx] = append([]byte(nil), shard...)
	if len(g.data) >= k {
		g.done = true
		g.data, g.par = nil, nil
		return nil
	}
	if len(g.data)+len(g.par) < k {
		return nil
	}
	size := len(shard)
	shards := make([][]byte, k+m)
	for i, p := range g.data {
		if i >= k || 2+len(p) > size {
			return nil
		}
		b := make([]byte, size)
		binary.BigEndian.PutUint16(b, uint16(len(p)))
		copy(b[2:], p)
		shards[i] = b
	}
	for i, s := range g.par {
		if len(s) != size {
			return nil
		}
		shards[k+i] = s
	}
	enc, err := rsFor(k, m)
	if err != nil {
		return nil
	}
	if err := enc.ReconstructData(shards); err != nil {
		return nil
	}
	var out [][]byte
	for i := 0; i < k; i++ {
		if _, have := g.data[i]; have {
			continue
		}
		n := int(binary.BigEndian.Uint16(shards[i]))
		if 2+n > len(shards[i]) {
			continue
		}
		pkt := append([]byte(nil), shards[i][2:2+n]...)
		g.seen[i] = true
		out = append(out, pkt)
	}
	g.done = true
	g.data, g.par = nil, nil
	return out
}

func (d *fecDec) expire(now time.Time) {
	for len(d.order) > 0 {
		g := d.groups[d.order[0]]
		if g != nil && now.Sub(g.t) < 3*time.Second {
			break
		}
		delete(d.groups, d.order[0])
		d.order = d.order[1:]
	}
}
