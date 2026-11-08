package main

import (
	"net"
	"net/netip"
	"testing"
	"time"
)

func ip4(a, b, c, d byte) netip.Addr { return netip.AddrFrom4([4]byte{a, b, c, d}) }

func TestLimiterPerAddress(t *testing.T) {
	l := newHSLimiter()
	now := time.Unix(1000, 0)
	a := ip4(203, 0, 113, 5)
	ok := 0
	for i := 0; i < 50; i++ {
		if l.allow(a, now) {
			ok++
		}
	}
	if ok != int(perIPBurst) {
		t.Fatalf("за один миг с одного адреса должно пройти %v рукопожатий, прошло %d", perIPBurst, ok)
	}
	// другой адрес не страдает
	if !l.allow(ip4(203, 0, 113, 6), now) {
		t.Fatal("чужой адрес не должен страдать из-за соседа")
	}
	// через секунду добавляется perIPRate
	now = now.Add(time.Second)
	got := 0
	for i := 0; i < 10; i++ {
		if l.allow(a, now) {
			got++
		}
	}
	if got != int(perIPRate) {
		t.Fatalf("через секунду должно прибавиться %v, прибавилось %d", perIPRate, got)
	}
	// после долгой тишины снова полный запас, но не больше
	now = now.Add(time.Hour)
	got = 0
	for i := 0; i < 50; i++ {
		if l.allow(a, now) {
			got++
		}
	}
	if got != int(perIPBurst) {
		t.Fatalf("запас не должен копиться выше %v, а прошло %d", perIPBurst, got)
	}
}

func TestLimiterGlobal(t *testing.T) {
	l := newHSLimiter()
	now := time.Unix(2000, 0)
	ok := 0
	for i := 0; i < 1000; i++ { // 1000 разных адресов в один миг (рассылка с подменёнными адресами)
		if l.allow(ip4(10, byte(i>>8), byte(i), 1), now) {
			ok++
		}
	}
	if ok != int(globalBurst) {
		t.Fatalf("на весь сервер за миг должно пройти %v, прошло %d", globalBurst, ok)
	}
	now = now.Add(time.Second)
	ok = 0
	for i := 0; i < 1000; i++ {
		if l.allow(ip4(11, byte(i>>8), byte(i), 1), now) {
			ok++
		}
	}
	if ok != int(globalRate) {
		t.Fatalf("через секунду должно прибавиться %v, прибавилось %d", globalRate, ok)
	}
}

func TestLimiterMemoryIsBounded(t *testing.T) {
	l := newHSLimiter()
	l.gRate, l.gBurst = 1e9, 1e9 // общий предел не мешает проверке таблицы
	now := time.Unix(3000, 0)
	for i := 0; i < maxTracked+500; i++ {
		l.allow(ip4(172, byte(i>>16), byte(i>>8), byte(i)), now)
	}
	if len(l.perIP) > maxTracked {
		t.Fatalf("таблица выросла до %d записей (предел %d)", len(l.perIP), maxTracked)
	}
	// таблица полна, новый адрес всё равно проходит по общему пределу
	if !l.allow(ip4(192, 168, 7, 7), now) {
		t.Fatal("при полной таблице новый адрес должен проходить по общему пределу")
	}
	// через минуту старые записи убираются
	now = now.Add(trackedTTL + 2*limiterGCGap)
	l.allow(ip4(192, 168, 7, 8), now)
	if len(l.perIP) > 2 {
		t.Fatalf("старые записи должны были уйти, осталось %d", len(l.perIP))
	}
}

// Настоящий сервер: поток «правильно упакованных, но пустых» рукопожатий не заставляет его считать кривую сто раз —
// лишние отбрасываются и считаются в статусе.
func TestServerDropsHandshakeFlood(t *testing.T) {
	p := newPair(t, "off", 0, nil)
	obf := obfKeyFor(pubOf(t, p.srv))
	c, err := net.DialUDP("udp", nil, &net.UDPAddr{IP: net.IPv4(127, 0, 0, 1), Port: p.srv.LocalPort()})
	if err != nil {
		t.Fatal(err)
	}
	defer c.Close()
	const sent = 100
	for i := 0; i < sent; i++ {
		junk := make([]byte, 120)
		for j := range junk {
			junk[j] = byte(i + j*7)
		}
		if _, err := c.Write(sealHandshake(&obf, junk)); err != nil {
			t.Fatal(err)
		}
	}
	want := uint64(sent) - uint64(perIPBurst) - 5
	deadline := time.Now().Add(3 * time.Second)
	for p.srv.hsDrop.Load() < want && time.Now().Before(deadline) {
		time.Sleep(20 * time.Millisecond)
	}
	dropped := p.srv.hsDrop.Load()
	if dropped < want {
		t.Fatalf("из %d пакетов должно отбросить почти все, кроме запаса в %v, а отброшено %d", sent, perIPBurst, dropped)
	}
	if st := p.srv.Status(); st.HsDrop < dropped {
		t.Fatalf("в статусе %d, а на деле не меньше %d", st.HsDrop, dropped)
	}
}

func pubOf(t *testing.T, e *Engine) []byte {
	t.Helper()
	return e.static.Public
}
