package main

import (
	"bytes"
	crand "crypto/rand"
	"testing"
	"time"
)

func betaKeys(t *testing.T) []byte {
	k := make([]byte, 32)
	if _, err := crand.Read(k); err != nil {
		t.Fatal(err)
	}
	return k
}

func betaPair(t *testing.T, srvKey, cliKey []byte) *pair {
	return newPair(t, "off", 0, func(srv, cli *Config) { srv.BetaKey = srvKey; cli.BetaKey = cliKey; cli.Proto = 3 })
}

// GSP Beta: клиент версии 3 и сервер с тем же секретом — три сообщения, данные в обе стороны, сеанс версии 3 с обеих сторон.
func TestProtocolV3EndToEnd(t *testing.T) {
	k := betaKeys(t)
	p := betaPair(t, k, k)
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

// Другой секрет профиля: сервер не отвечает ничем, сеанса нет ни у кого.
func TestBetaWrongSecretGetsSilence(t *testing.T) {
	p := betaPair(t, betaKeys(t), betaKeys(t))
	for i := 0; i < 30; i++ {
		p.cliDev.in <- mkIP(cliIP, srvIP, uint32(i), 100)
		time.Sleep(100 * time.Millisecond)
	}
	if p.cli.peerOnly().cur.Load() != nil || p.srv.peerOnly().cur.Load() != nil {
		t.Fatal("с неверным секретом сеанс не должен был установиться")
	}
	if len(p.srv.betaTab.m) != 0 {
		t.Fatal("сервер не должен хранить состояние для пакетов с неверным секретом")
	}
}

// Сервер без BetaKey версию 3 не знает и молчит; клиент без BetaKey с Proto = 3 не запускается.
func TestBetaNeedsKey(t *testing.T) {
	p := betaPair(t, nil, betaKeys(t))
	for i := 0; i < 15; i++ {
		p.cliDev.in <- mkIP(cliIP, srvIP, uint32(i), 100)
		time.Sleep(100 * time.Millisecond)
	}
	if p.srv.peerOnly().cur.Load() != nil {
		t.Fatal("сервер без BetaKey принял версию 3")
	}
	sk, _ := newKeypair()
	ck, _ := newKeypair()
	_, err := NewEngine(Config{PrivateKey: ck.Private, Proto: 3, Peers: []PeerConfig{{PublicKey: sk.Public}}}, newFakeDev())
	if err == nil {
		t.Fatal("клиент с Proto = 3 без BetaKey должен отказаться запускаться")
	}
}

// Обёртка: туда и обратно, и чужие пакеты не принимаются.
func TestBetaWrapUnwrap(t *testing.T) {
	body := make([]byte, 300)
	fillRand(body)
	pk := wrapBeta(body)
	got, ok := unwrapBeta(pk)
	if !ok || !bytes.Equal(got, body) {
		t.Fatal("обёртка не вернула то, что было внутри")
	}
	if pk[0]&0xC0 != 0xC0 || pk[4] != 1 {
		t.Fatal("заголовок не похож на длинный заголовок QUIC версии 1")
	}
	bad := append([]byte(nil), pk...)
	bad[24] ^= 0x01
	if _, ok := unwrapBeta(bad); ok {
		t.Fatal("пакет с неверной длиной принят")
	}
	for i := 0; i < 200; i++ {
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

// sealBeta/openBeta: типы и индексы доходят, размер 1200–1300, неверный ключ не открывает.
func TestBetaSealOpen(t *testing.T) {
	k1 := obfKeyBeta(make([]byte, 32), betaKeys(t))
	k2 := obfKeyBeta(make([]byte, 32), betaKeys(t))
	msg := []byte("привет, noise")
	for typ := byte(1); typ <= 3; typ++ {
		pk := sealBeta(&k1, typ, 0xA1B2C3D4, msg)
		if len(pk) < betaMinTotal || len(pk) > betaMaxTotal {
			t.Fatalf("размер %d вне 1200–1300", len(pk))
		}
		gt, gi, gm, ok := openBeta(&k1, pk)
		if !ok || gt != typ || gi != 0xA1B2C3D4 || !bytes.Equal(gm, msg) {
			t.Fatal("сообщение не дошло как было")
		}
		if _, _, _, ok := openBeta(&k2, pk); ok {
			t.Fatal("пакет открылся чужим ключом")
		}
	}
}

// Что видно в сети: мусорные пакеты, потом три сообщения рукопожатия в форме QUIC Initial, размером 1200–1300 (третье — дважды).
func TestBetaHandshakeOnTheWire(t *testing.T) {
	k := betaKeys(t)
	p := betaPair(t, k, k)
	exchange(t, p)
	var all [][]byte
	for len(p.rel.capture) > 0 {
		all = append(all, <-p.rel.capture)
	}
	first := -1
	shaped := 0
	for i, b := range all {
		if _, ok := unwrapBeta(b); ok {
			if first < 0 {
				first = i
			}
			shaped++
			if len(b) < betaMinTotal || len(b) > betaMaxTotal {
				t.Fatalf("размер пакета рукопожатия %d вне 1200–1300", len(b))
			}
		}
	}
	if first < betaJunkMin || first > betaJunkMax {
		t.Fatalf("перед рукопожатием %d мусорных пакетов, ждали %d–%d", first, betaJunkMin, betaJunkMax)
	}
	if shaped < 3 { // клиент → сервер: первое сообщение и два экземпляра третьего
		t.Fatalf("в сети %d пакетов рукопожатия, ждали не меньше 3", shaped)
	}
}

// Повтор записанного третьего сообщения не создаёт второй сеанс и не ломает первый.
func TestBetaReplayOfFinalIgnored(t *testing.T) {
	k := betaKeys(t)
	p := betaPair(t, k, k)
	exchange(t, p)
	before := p.srv.peerOnly().cur.Load()
	var fins [][]byte
	for len(p.rel.capture) > 0 {
		b := <-p.rel.capture
		if _, ok := unwrapBeta(b); ok {
			fins = append(fins, b)
		}
	}
	ua := p.srv.peerOnly().endpoint.Load()
	for _, b := range fins {
		p.srv.handleHandshake(b, ua.AddrPort(), ua)
	}
	if p.srv.peerOnly().cur.Load() != before {
		t.Fatal("повтор записанных пакетов рукопожатия создал новый сеанс")
	}
}

// Прежние клиенты (версии 1 и 2) работают с сервером, у которого включена Beta.
func TestBetaServerStillServesOldClients(t *testing.T) {
	for _, v := range []int{0, 2} {
		v := v
		k := betaKeys(t)
		p := newPair(t, "off", 0, func(srv, cli *Config) { srv.BetaKey = k; cli.Proto = v })
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

// Набивка у версии 3 есть по умолчанию (в данных разной длины появляются разные размеры).
func TestBetaDefaultPadding(t *testing.T) {
	k := betaKeys(t)
	p := betaPair(t, k, k)
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

func TestBetaKeysDifferByVersion(t *testing.T) {
	pub := make([]byte, 32)
	if string(prologueFor(protoV3)) == string(prologueFor(protoV2)) {
		t.Fatal("прологи версий 2 и 3 должны отличаться")
	}
	a, b := betaKeys(t), betaKeys(t)
	if obfKeyBeta(pub, a) == obfKeyBeta(pub, b) {
		t.Fatal("разные секреты дали одинаковый ключ упаковки")
	}
	if obfKeyBeta(pub, a) == obfKeyFor(pub, protoV2) {
		t.Fatal("ключ упаковки Beta совпал с ключом версии 2")
	}
}
