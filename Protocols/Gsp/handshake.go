package main

// Рукопожатие GSP — шаблон Noise IK (Curve25519, ChaCha20-Poly1305, BLAKE2s):
//   <- s                       (клиент заранее знает открытый ключ сервера)
//   -> e, es, s, ss  + {время, наш индекс, возможности}
//   <- e, ee, se     + {индекс сервера, возможности}
// Оба пакета дополнительно упаковываются sealHandshake (см. crypto.go).

import (
	crand "crypto/rand"
	"encoding/binary"
	"errors"
	"time"

	"github.com/flynn/noise"
	"golang.org/x/crypto/blake2s"
)

var prologue = []byte("ghostshell-gsp-v1")

type pendingHS struct {
	hs   *noise.HandshakeState
	idx  uint32
	sent time.Time
}

func newHS(initiator bool, static noise.DHKey, peerStatic []byte) (*noise.HandshakeState, error) {
	return noise.NewHandshakeState(noise.Config{
		CipherSuite:   noiseSuite,
		Random:        crand.Reader,
		Pattern:       noise.HandshakeIK,
		Initiator:     initiator,
		Prologue:      prologue,
		StaticKeypair: static,
		PeerStatic:    peerStatic,
	})
}

// 12 байт: секунды и доли секунды. Сервер принимает только строго возрастающие значения (защита от повтора).
func timestamp12() (t [12]byte) {
	now := time.Now()
	binary.BigEndian.PutUint64(t[0:8], uint64(now.Unix()))
	binary.BigEndian.PutUint32(t[8:12], uint32(now.Nanosecond()))
	return
}

func tsLess(a, b [12]byte) bool {
	for i := range a {
		if a[i] != b[i] {
			return a[i] < b[i]
		}
	}
	return false
}

// Возможности (на будущее): бит 0 — понимаю защиту от потерь.
const capFEC = 1

func buildInit(static noise.DHKey, serverPub []byte, myIdx uint32) (*pendingHS, []byte, error) {
	hs, err := newHS(true, static, serverPub)
	if err != nil {
		return nil, nil, err
	}
	var pl [18]byte
	ts := timestamp12()
	copy(pl[0:12], ts[:])
	binary.BigEndian.PutUint32(pl[12:16], myIdx)
	binary.BigEndian.PutUint16(pl[16:18], capFEC)
	msg, _, _, err := hs.WriteMessage(nil, pl[:])
	if err != nil {
		return nil, nil, err
	}
	return &pendingHS{hs: hs, idx: myIdx, sent: time.Now()}, msg, nil
}

type initInfo struct {
	hs        *noise.HandshakeState
	clientPub []byte
	ts        [12]byte
	clientIdx uint32
	caps      uint16
}

func parseInit(static noise.DHKey, msg []byte) (*initInfo, error) {
	hs, err := newHS(false, static, nil)
	if err != nil {
		return nil, err
	}
	pl, _, _, err := hs.ReadMessage(nil, msg)
	if err != nil {
		return nil, err
	}
	if len(pl) < 18 {
		return nil, errors.New("короткая нагрузка рукопожатия")
	}
	info := &initInfo{hs: hs, clientPub: append([]byte(nil), hs.PeerStatic()...)}
	copy(info.ts[:], pl[0:12])
	info.clientIdx = binary.BigEndian.Uint32(pl[12:16])
	info.caps = binary.BigEndian.Uint16(pl[16:18])
	return info, nil
}

// Ответ сервера: сообщение для клиента и ключи сеанса.
func buildResponse(info *initInfo, serverIdx uint32) (msg []byte, send, recv, sendHP, recvHP [32]byte, err error) {
	var pl [6]byte
	binary.BigEndian.PutUint32(pl[0:4], serverIdx)
	binary.BigEndian.PutUint16(pl[4:6], capFEC)
	m, cs1, cs2, err := info.hs.WriteMessage(nil, pl[:])
	if err != nil {
		return
	}
	if cs1 == nil || cs2 == nil {
		err = errors.New("рукопожатие не завершилось")
		return
	}
	msg = m
	send, recv, sendHP, recvHP = deriveKeys(cs1, cs2, false)
	return
}

// Клиент разбирает ответ сервера.
func parseResponse(p *pendingHS, msg []byte) (serverIdx uint32, send, recv, sendHP, recvHP [32]byte, err error) {
	pl, cs1, cs2, err := p.hs.ReadMessage(nil, msg)
	if err != nil {
		return
	}
	if cs1 == nil || cs2 == nil || len(pl) < 6 {
		err = errors.New("неполный ответ сервера")
		return
	}
	serverIdx = binary.BigEndian.Uint32(pl[0:4])
	send, recv, sendHP, recvHP = deriveKeys(cs1, cs2, true)
	return
}

// cs1 — от клиента к серверу, cs2 — обратно. Ключи заголовков выводятся отдельно от ключей данных.
func deriveKeys(cs1, cs2 *noise.CipherState, initiator bool) (send, recv, sendHP, recvHP [32]byte) {
	k1, k2 := cs1.UnsafeKey(), cs2.UnsafeKey()
	hp := func(k [32]byte) [32]byte { return blake2s.Sum256(append(k[:], []byte("gsp-hp-v1")...)) }
	if initiator {
		return k1, k2, hp(k1), hp(k2)
	}
	return k2, k1, hp(k2), hp(k1)
}
