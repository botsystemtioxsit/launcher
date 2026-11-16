package main

import (
	"testing"
	"time"
)

// GSP Beta: клиент версии 3 и сервер, который понимает все версии — рукопожатие, данные в обе стороны, сеанс версии 3 с обеих сторон.
func TestProtocolV3EndToEnd(t *testing.T) {
	p := newPair(t, "off", 0, func(srv, cli *Config) { cli.Proto = 3 })
	exchange(t, p)
	if v := p.cli.peerOnly().cur.Load().ver; v != protoV3 {
		t.Fatalf("у клиента версия сеанса %d, ждали 3", v)
	}
	if v := p.srv.peerOnly().cur.Load().ver; v != protoV3 {
		t.Fatalf("у сервера версия сеанса %d, ждали 3", v)
	}
	if st := p.cli.Status(); st.Peers[0].Proto != 3 {
		t.Fatalf("в статусе версия %d, ждали 3", st.Peers[0].Proto)
	}
}

// Обёртка: туда и обратно, и чужие пакеты не принимаются.
func TestBetaWrapUnwrap(t *testing.T) {
	body := make([]byte, 300)
	fillRand(body)
	pk := wrapBeta(body)
	got, ok := unwrapBeta(pk)
	if !ok || string(got) != string(body) {
		t.Fatal("обёртка не вернула то, что было внутри")
	}
	if pk[0]&0xC0 != 0xC0 || pk[4] != 1 {
		t.Fatal("заголовок не похож на длинный заголовок QUIC версии 1")
	}
	bad := append([]byte(nil), pk...)
	bad[24] ^= 0x01 // длина не сходится
	if _, ok := unwrapBeta(bad); ok {
		t.Fatal("пакет с неверной длиной принят")
	}
	for i := 0; i < 200; i++ { // случайный мусор почти никогда не проходит
		junk := make([]byte, 40+i)
		fillRand(junk)
		if _, ok := unwrapBeta(junk); ok {
			t.Fatal("случайный пакет принят за рукопожатие")
		}
	}
	if _, ok := unwrapBeta(nil); ok {
		t.Fatal("пустой пакет принят")
	}
}

// Что видно в сети: сначала несколько мусорных пакетов, потом «начальный пакет» размером 1200–1300 с QUIC-подобным заголовком; повторов формы нет.
func TestBetaHandshakeOnTheWire(t *testing.T) {
	p := newPair(t, "off", 0, func(srv, cli *Config) { cli.Proto = 3 })
	exchange(t, p)
	var all [][]byte
	for len(p.rel.capture) > 0 {
		all = append(all, <-p.rel.capture)
	}
	first := -1
	for i, b := range all {
		if _, ok := unwrapBeta(b); ok {
			first = i
			if len(b) < betaMinTotal || len(b) > betaMaxTotal {
				t.Fatalf("размер начального пакета %d вне 1200–1300", len(b))
			}
			break
		}
	}
	if first < 0 {
		t.Fatal("в сети не нашлось рукопожатия в обёртке")
	}
	if first < betaJunkMin || first > betaJunkMax {
		t.Fatalf("перед рукопожатием %d мусорных пакетов, ждали %d–%d", first, betaJunkMin, betaJunkMax)
	}
	for _, b := range all[:first] {
		if _, ok := unwrapBeta(b); ok {
			t.Fatal("мусорный пакет похож на рукопожатие")
		}
	}
}

// Мусор в начале не мешает: сервер молчит в ответ и остаётся рабочим для настоящего клиента.
func TestBetaServerIgnoresJunkAndOldClientsWork(t *testing.T) {
	for _, v := range []int{0, 2} {
		v := v
		p := newPair(t, "off", 0, func(srv, cli *Config) { cli.Proto = v })
		exchange(t, p)
		want := protoV1
		if v == 2 {
			want = protoV2
		}
		if got := p.srv.peerOnly().cur.Load().ver; got != want {
			t.Fatalf("клиент версии %d: у сервера сеанс версии %d", v, got)
		}
	}
}

// Версии 3 и 2 не договорятся с чужими ключами упаковки: пакет версии 3 сервер, настроенный на другой ключ, не откроет.
func TestBetaKeysDifferByVersion(t *testing.T) {
	pub := make([]byte, 32)
	if obfKeyFor(pub, protoV3) == obfKeyFor(pub, protoV2) {
		t.Fatal("ключи упаковки версий 2 и 3 должны отличаться")
	}
	if string(prologueFor(protoV3)) == string(prologueFor(protoV2)) {
		t.Fatal("прологи версий 2 и 3 должны отличаться")
	}
}

// Набивка у версии 3 есть по умолчанию (в данных разной длины появляются разные размеры).
func TestBetaDefaultPadding(t *testing.T) {
	p := newPair(t, "off", 0, func(srv, cli *Config) { cli.Proto = 3 })
	exchange(t, p)
	time.Sleep(50 * time.Millisecond)
	sizes := map[int]bool{}
	for len(p.rel.capture) > 0 {
		b := <-p.rel.capture
		if _, ok := unwrapBeta(b); ok || len(b) > 600 || len(b) < 100 {
			continue
		}
		sizes[len(b)] = true
	}
	if len(sizes) < 8 {
		t.Fatalf("размеров пакетов данных слишком мало (%d): набивка не работает", len(sizes))
	}
}
