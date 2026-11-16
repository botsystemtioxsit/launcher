package main

// «GSP Beta» (версия протокола 3) — тестовый протокол, нацеленный на сети с глубоким анализом трафика (в том числе ТСПУ).
// Это НЕ WireGuard в другой обёртке: схема другая. Но шифры — только стандартные (ChaCha20-Poly1305, Curve25519, BLAKE2s, HKDF-SHA256, шаблон Noise):
// придумывать собственные шифры нельзя — их почти всегда ломают, а проект защищает людей.
//
// Чем отличается от WireGuard и от прежних версий GSP:
//  1. Другое рукопожатие: Noise XKpsk3, три сообщения (у WireGuard и GSP 1–2 — IKpsk2/IK, два). Личность клиента открывается только в третьем сообщении.
//  2. Секрет профиля BetaKey (у каждого сервера свой): входит в рукопожатие и в ключ упаковки пакетов. Кто его не знает — не получает от сервера ни байта
//     (пробы ТСПУ и сканеры видят молчащий UDP-порт), а перебрать ключ по записанным пакетам нельзя.
//  3. Все три сообщения рукопожатия — в форме QUIC Initial (заголовок, размер 1200–1300 байт). Это ПОХОЖЕСТЬ ФОРМЫ, а не настоящий QUIC.
//  4. Перед рукопожатием — 3–5 мусорных пакетов случайной длины; набивка пакетов данных по умолчанию; смена ключей и keepalive с разбросом по времени (нет постоянного ритма).
//
// Что НЕ сделано: запасной режим через TCP/443, смена портов, проверка на настоящем ТСПУ.

import (
	"crypto/hkdf"
	"crypto/sha256"
	"encoding/binary"
	"net"
	"sync"
	"time"

	"github.com/flynn/noise"
)

const (
	betaHdrLen   = 26   // флаги(1) + версия(4) + длина DCID(1) + DCID(8) + длина SCID(1) + SCID(8) + токен(1) + длина(2)
	betaMinTotal = 1200 // как минимальный размер Initial-датаграммы у QUIC
	betaMaxTotal = 1300
	betaPad      = 32 // набивка пакетов данных по умолчанию в версии 3
	betaJunkMin  = 3
	betaJunkMax  = 5
)

// randIn — случайное число из [lo, hi].
func randIn(lo, hi int) int {
	if hi <= lo {
		return lo
	}
	return lo + int(randU32()%uint32(hi-lo+1))
}

// wrapBeta добавляет QUIC-подобный заголовок к упакованному рукопожатию.
func wrapBeta(sealed []byte) []byte {
	out := make([]byte, betaHdrLen, betaHdrLen+len(sealed))
	var rnd [18]byte
	fillRand(rnd[:])
	out[0] = 0xC0 | (rnd[0] & 0x03) // длинный заголовок, тип Initial, длина номера пакета — случайная
	out[1], out[2], out[3], out[4] = 0, 0, 0, 1
	out[5] = 8
	copy(out[6:14], rnd[1:9])
	out[14] = 8
	copy(out[15:23], rnd[9:17])
	out[23] = 0 // длина токена
	n := len(sealed)
	out[24] = 0x40 | byte(n>>8) // длина в два байта (QUIC-число переменной длины)
	out[25] = byte(n)
	return append(out, sealed...)
}

// unwrapBeta проверяет форму и возвращает упакованное рукопожатие.
func unwrapBeta(pk []byte) ([]byte, bool) {
	if len(pk) < betaHdrLen+1 || pk[0]&0xFC != 0xC0 || pk[1] != 0 || pk[2] != 0 || pk[3] != 0 || pk[4] != 1 || pk[5] != 8 || pk[14] != 8 || pk[23] != 0 || pk[24]&0xC0 != 0x40 {
		return nil, false
	}
	n := int(pk[24]&0x3F)<<8 | int(pk[25])
	if n != len(pk)-betaHdrLen {
		return nil, false
	}
	return pk[betaHdrLen:], true
}

func fillRand(b []byte) {
	for i := 0; i < len(b); i += 4 {
		v := randU32()
		for j := 0; j < 4 && i+j < len(b); j++ {
			b[i+j] = byte(v >> (8 * j))
		}
	}
}

// sendBetaJunk отправляет несколько случайных пакетов до рукопожатия.
func sendBetaJunk(c *net.UDPConn, to *net.UDPAddr) {
	n := randIn(betaJunkMin, betaJunkMax)
	for i := 0; i < n; i++ {
		b := make([]byte, randIn(40, 1100))
		fillRand(b)
		_, _ = c.WriteToUDP(b, to)
	}
}

// Ключ упаковки рукопожатия Beta: из секрета профиля, «соль» — открытый ключ сервера (у каждого сервера свой).
func obfKeyBeta(serverPub, betaKey []byte) [32]byte {
	k, _ := hkdf.Key(sha256.New, betaKey, serverPub, "gsp beta v3 wire", 32)
	var out [32]byte
	copy(out[:], k)
	return out
}

// Типы сообщений рукопожатия Beta.
const (
	bMsgInit = 1 // клиент → сервер: e, es
	bMsgResp = 2 // сервер → клиент: e, ee
	bMsgFin  = 3 // клиент → сервер: s, se (+ секрет профиля)
)

// sealBeta: QUIC-подобный заголовок + XChaCha20-Poly1305( тип(1) | индекс(4) | длина(2) | сообщение Noise | набивка до размера 1200–1300 ).
func sealBeta(key *[32]byte, typ byte, idx uint32, msg []byte) []byte {
	const inner = 1 + 4 + 2
	fixed := betaHdrLen + 24 + inner + len(msg) + tagLen
	pad := randIn(betaMinTotal, betaMaxTotal) - fixed
	if pad < 0 {
		pad = 0
	}
	plain := make([]byte, inner+len(msg)+pad)
	plain[0] = typ
	binary.BigEndian.PutUint32(plain[1:5], idx)
	binary.BigEndian.PutUint16(plain[5:7], uint16(len(msg)))
	copy(plain[inner:], msg)
	fillRand(plain[inner+len(msg):])
	return wrapBeta(sealRaw(key, plain))
}

// openBeta возвращает тип, индекс и сообщение Noise; ok=false — пакет не наш (молчим).
func openBeta(key *[32]byte, pk []byte) (typ byte, idx uint32, msg []byte, ok bool) {
	body, shaped := unwrapBeta(pk)
	if !shaped {
		return 0, 0, nil, false
	}
	plain, err := openRaw(key, body)
	if err != nil || len(plain) < 7 {
		return 0, 0, nil, false
	}
	n := int(binary.BigEndian.Uint16(plain[5:7]))
	if 7+n > len(plain) {
		return 0, 0, nil, false
	}
	return plain[0], binary.BigEndian.Uint32(plain[1:5]), plain[7 : 7+n], true
}

// Состояние сервера между первым и третьим сообщением рукопожатия.
type betaPend struct {
	hs        *noise.HandshakeState
	clientIdx uint32
	ts        [12]byte
	created   time.Time
}

type betaTable struct {
	mu sync.Mutex
	m  map[uint32]*betaPend
}

const (
	betaPendMax = 512
	betaPendTTL = 15 * time.Second
)

func (t *betaTable) put(idx uint32, p *betaPend) bool {
	t.mu.Lock()
	defer t.mu.Unlock()
	if t.m == nil {
		t.m = map[uint32]*betaPend{}
	}
	now := time.Now()
	for k, v := range t.m {
		if now.Sub(v.created) > betaPendTTL {
			delete(t.m, k)
		}
	}
	if len(t.m) >= betaPendMax {
		return false
	}
	t.m[idx] = p
	return true
}

func (t *betaTable) take(idx uint32) *betaPend {
	t.mu.Lock()
	defer t.mu.Unlock()
	p := t.m[idx]
	delete(t.m, idx)
	if p != nil && time.Since(p.created) > betaPendTTL {
		return nil
	}
	return p
}
