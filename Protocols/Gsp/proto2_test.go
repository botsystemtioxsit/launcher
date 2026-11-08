package main

import (
	"bytes"
	"encoding/hex"
	"testing"
	"time"
)

// Контрольный пример из RFC 9001, приложение A.5 (ChaCha20-Poly1305): та же конструкция защиты заголовка, что у нас в версии 2.
// Ключ защиты заголовка, образец шифртекста → первые 5 байт маски aefefe7d03.
func TestHeaderProtectionMatchesRFC9001(t *testing.T) {
	hpb, _ := hex.DecodeString("25a282b9e82f06f21f488917a4fc8f1b73573685608597d0efcb076b0ab7a7a4")
	sample, _ := hex.DecodeString("5e5cd55c41f69080575d7999c25a5bfb")
	var hp [32]byte
	copy(hp[:], hpb)
	m := hpMask(protoV2, &hp, sample)
	if got := hex.EncodeToString(m[:5]); got != "aefefe7d03" {
		t.Fatalf("маска по RFC 9001: ждали aefefe7d03, получили %s", got)
	}
	// версия 1 — другая функция
	m1 := hpMask(protoV1, &hp, sample)
	if bytes.Equal(m[:], m1[:]) {
		t.Fatal("версии 1 и 2 не должны давать одинаковую маску")
	}
}

func TestKeyDerivationDiffersByVersion(t *testing.T) {
	var k [32]byte
	for i := range k {
		k[i] = byte(i * 7)
	}
	a, b := hpKeyFor(protoV1, k), hpKeyFor(protoV2, k)
	if a == b || a == k || b == k {
		t.Fatal("ключи защиты заголовка должны отличаться и от ключа данных, и друг от друга")
	}
	if hpKeyFor(protoV2, k) != b {
		t.Fatal("вывод ключа должен быть детерминированным")
	}
	pub := k[:]
	if obfKeyFor(pub, protoV1) == obfKeyFor(pub, protoV2) {
		t.Fatal("ключи упаковки рукопожатия версий должны отличаться")
	}
}

func exchange(t *testing.T, p *pair) {
	t.Helper()
	deadline := time.Now().Add(5 * time.Second)
	for p.cli.peerOnly().cur.Load() == nil && time.Now().Before(deadline) {
		p.cliDev.in <- mkIP(cliIP, srvIP, 0, 100)
		time.Sleep(50 * time.Millisecond)
	}
	if p.cli.peerOnly().cur.Load() == nil {
		t.Fatal("сеанс не установился")
	}
	for i := uint32(1); i <= 30; i++ {
		p.cliDev.in <- mkIP(cliIP, srvIP, i, 200)
	}
	if got := recvN(p.srvDev, 30, 3*time.Second); len(got) < 30 {
		t.Fatalf("на сервер дошло %d из 30", len(got))
	}
	for i := uint32(1); i <= 30; i++ {
		p.srvDev.in <- mkIP(srvIP, cliIP, i, 300)
	}
	if back := recvN(p.cliDev, 30, 3*time.Second); len(back) != 30 {
		t.Fatalf("на клиент дошло %d из 30", len(back))
	}
}

// Клиент версии 2 и сервер, который понимает обе: рукопожатие, данные в обе стороны, сеанс версии 2 с обеих сторон.
func TestProtocolV2EndToEnd(t *testing.T) {
	p := newPair(t, "off", 0, func(srv, cli *Config) { cli.Proto = 2 })
	exchange(t, p)
	if v := p.cli.peerOnly().cur.Load().ver; v != protoV2 {
		t.Fatalf("у клиента версия сеанса %d, ждали 2", v)
	}
	if v := p.srv.peerOnly().cur.Load().ver; v != protoV2 {
		t.Fatalf("у сервера версия сеанса %d, ждали 2", v)
	}
	if st := p.cli.Status(); st.Peers[0].Proto != 2 {
		t.Fatalf("в статусе версия %d, ждали 2", st.Peers[0].Proto)
	}
}

// Прежний клиент (без Proto) продолжает работать с тем же сервером: версия 1.
func TestProtocolV1ClientStillWorks(t *testing.T) {
	p := newPair(t, "off", 0, nil)
	exchange(t, p)
	if v := p.cli.peerOnly().cur.Load().ver; v != protoV1 {
		t.Fatalf("у клиента без Proto версия сеанса %d, ждали 1", v)
	}
	if v := p.srv.peerOnly().cur.Load().ver; v != protoV1 {
		t.Fatalf("сервер должен отвечать на версии клиента (1), а сеанс версии %d", v)
	}
}

// Пакеты данных версии 2 по-прежнему выглядят случайными: первые байты не содержат индекса и счётчика в открытом виде.
func TestProtocolV2HeaderLooksRandom(t *testing.T) {
	p := newPair(t, "off", 0, func(srv, cli *Config) { cli.Proto = 2 })
	exchange(t, p)
	seen := map[[4]byte]bool{}
	n := 0
	for len(p.rel.capture) > 0 && n < 200 {
		b := <-p.rel.capture
		if len(b) < 16 {
			continue
		}
		var k [4]byte
		copy(k[:], b[:4])
		seen[k] = true
		n++
	}
	if n < 20 || len(seen) < n*9/10 {
		t.Fatalf("первые 4 байта пакетов слишком часто повторяются: %d разных из %d", len(seen), n)
	}
}

func BenchmarkHeaderMaskV1(b *testing.B) { benchMask(b, protoV1) }
func BenchmarkHeaderMaskV2(b *testing.B) { benchMask(b, protoV2) }

func benchMask(b *testing.B, ver int) {
	var hp [32]byte
	sample := make([]byte, 16)
	b.ReportAllocs()
	for i := 0; i < b.N; i++ {
		sample[0] = byte(i)
		_ = hpMask(ver, &hp, sample)
	}
}
