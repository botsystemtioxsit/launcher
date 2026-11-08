package main

// Кирпичики протокола GSP: ключи, обфускация рукопожатия, защита заголовка,
// шифрование пакетов данных и окно защиты от повторов.
//
// Своей криптографии здесь нет: только проверенные примитивы
// (Curve25519, ChaCha20-Poly1305, XChaCha20-Poly1305, BLAKE2s, шаблон Noise IK).

import (
	"crypto/cipher"
	"crypto/hkdf"
	crand "crypto/rand"
	"crypto/sha256"
	"encoding/base64"
	"encoding/binary"
	"errors"
	"sync"
	"sync/atomic"
	"time"

	"github.com/flynn/noise"
	"golang.org/x/crypto/blake2s"
	"golang.org/x/crypto/chacha20"
	"golang.org/x/crypto/chacha20poly1305"
	"golang.org/x/crypto/curve25519"
)

const (
	hdrLen   = 12 // индекс получателя (4) + счётчик (8), в пакете замаскированы
	tagLen   = 16
	bodyOff  = hdrLen + 2 // с этого места в буфере лежит IP-пакет (после типа и длины набивки)
	minPlain = 16         // образец для маски заголовка берётся из шифртекста, он должен быть ≥ 16 байт
	bufSize  = 2048
)

// Версии протокола.
//
//	1 — GSP 0.2: маска заголовка = BLAKE2s(ключ ‖ образец), ключи заголовков и ключ маскировки рукопожатия — BLAKE2s от ключа с «солью»-строкой.
//	2 — GSP 0.3: только стандартные конструкции — защита заголовка на ChaCha20 как в QUIC (RFC 9001, §5.4.4) и ключи через HKDF-SHA256 (RFC 5869);
//	    в прологе Noise другая строка, поэтому клиент версии 2 и сервер версии 1 (и наоборот) друг с другом не договорятся.
//
// Сервер принимает обе версии (смотрит, чьим ключом упаковано рукопожатие), клиент говорит ровно на той версии, что указана в его настройках
// (`Proto`, по умолчанию 1 — чтобы прежние профили продолжали работать). Новые профили, которые выдаёт скрипт сервера, — версии 2.
const (
	protoV1 = 1
	protoV2 = 2
)

// Типы кадров внутри шифртекста.
const (
	frData   = 0 // IP-пакет
	frKeep   = 1 // «я жив»
	frFecDat = 2 // IP-пакет в группе защиты от потерь (в конце 5 байт: номер группы и место в группе)
	frFecPar = 3 // проверочный кусок группы
	frReport = 4 // отчёт о потерях: сколько пакетов дошло из скольки ожидалось
	frClose  = 5 // закрытие сеанса
)

var noiseSuite = noise.NewCipherSuite(noise.DH25519, noise.CipherChaChaPoly, noise.HashBLAKE2s)

var (
	errShort  = errors.New("короткий пакет")
	errIndex  = errors.New("не тот индекс сеанса")
	errReplay = errors.New("повтор или слишком старый пакет")
	errAuth   = errors.New("не прошла проверка подлинности")
)

func newKeypair() (noise.DHKey, error) { return noise.DH25519.GenerateKeypair(crand.Reader) }

func pubFromPriv(priv []byte) ([]byte, error) {
	return curve25519.X25519(priv, curve25519.Basepoint)
}

func keyToB64(k []byte) string { return base64.StdEncoding.EncodeToString(k) }

func keyFromB64(s string) ([]byte, error) {
	b, err := base64.StdEncoding.DecodeString(s)
	if err != nil || len(b) != 32 {
		return nil, errors.New("ключ должен быть 32 байта в base64")
	}
	return b, nil
}

// Ключ обфускации рукопожатия выводится из открытого ключа сервера: его знает каждый клиент,
// а посторонний не может даже получить ответ — пакет без правильной подписи молча отбрасывается.
// Это маскировка, а не секрет: открытый ключ сервера не тайна.
func obfKeyFor(serverPub []byte, ver int) [32]byte {
	if ver == protoV2 {
		k, _ := hkdf.Key(sha256.New, serverPub, nil, "gsp v2 handshake obfuscation", 32)
		var out [32]byte
		copy(out[:], k)
		return out
	}
	return blake2s.Sum256(append([]byte("gsp-obfs-v1"), serverPub...))
}

// Ключ защиты заголовка выводится из ключа данных (ключ — случайные 256 бит из Noise, поэтому HKDF-Expand без «извлечения» уместен).
func hpKeyFor(ver int, dataKey [32]byte) [32]byte {
	if ver == protoV2 {
		k, _ := hkdf.Expand(sha256.New, dataKey[:], "gsp v2 header protection", 32)
		var out [32]byte
		copy(out[:], k)
		return out
	}
	return blake2s.Sum256(append(append([]byte(nil), dataKey[:]...), []byte("gsp-hp-v1")...))
}

func randU32() uint32 {
	var b [4]byte
	_, _ = crand.Read(b[:])
	return binary.LittleEndian.Uint32(b[:])
}

func randByte() int {
	var b [1]byte
	_, _ = crand.Read(b[:])
	return int(b[0])
}

// sealHandshake: nonce(24) || XChaCha20-Poly1305( len(2) || сообщение Noise || случайная набивка ).
// Весь пакет выглядит как случайные байты, длина разная.
func sealHandshake(key *[32]byte, msg []byte) []byte {
	aead, _ := chacha20poly1305.NewX(key[:])
	pad := randByte() // 0..255
	plain := make([]byte, 2+len(msg)+pad)
	binary.BigEndian.PutUint16(plain, uint16(len(msg)))
	copy(plain[2:], msg)
	_, _ = crand.Read(plain[2+len(msg):])
	out := make([]byte, chacha20poly1305.NonceSizeX, chacha20poly1305.NonceSizeX+len(plain)+tagLen)
	_, _ = crand.Read(out)
	return aead.Seal(out, out[:chacha20poly1305.NonceSizeX], plain, nil)
}

func openHandshake(key *[32]byte, pkt []byte) ([]byte, error) {
	if len(pkt) < chacha20poly1305.NonceSizeX+2+tagLen {
		return nil, errShort
	}
	aead, _ := chacha20poly1305.NewX(key[:])
	plain, err := aead.Open(nil, pkt[:chacha20poly1305.NonceSizeX], pkt[chacha20poly1305.NonceSizeX:], nil)
	if err != nil {
		return nil, errAuth
	}
	n := int(binary.BigEndian.Uint16(plain))
	if 2+n > len(plain) {
		return nil, errShort
	}
	return plain[2 : 2+n], nil
}

// Маска заголовка. Первые 12 байт пакета (индекс и счётчик) в канале всегда случайны на вид.
//
// Версия 2 — ровно то, что описано в RFC 9001 §5.4.4 для ChaCha20: образец — первые 16 байт шифртекста, из него счётчик (4 байта, little-endian)
// и nonce (12 байт), маска — первые байты потока ChaCha20 по ключу защиты заголовка (у QUIC берут 5 байт, у нас 12 из того же блока).
// Версия 1 (прежняя) — BLAKE2s(ключ ‖ образец).
func hpMask(ver int, hp *[32]byte, sample []byte) (m [hdrLen]byte) {
	if ver == protoV2 {
		c, err := chacha20.NewUnauthenticatedCipher(hp[:], sample[4:16])
		if err != nil {
			return
		}
		c.SetCounter(binary.LittleEndian.Uint32(sample[0:4]))
		c.XORKeyStream(m[:], m[:])
		return
	}
	var in [48]byte
	copy(in[:32], hp[:])
	copy(in[32:], sample[:16])
	s := blake2s.Sum256(in[:])
	copy(m[:], s[:hdrLen])
	return
}

// Окно защиты от повторов на 2048 пакетов (как в WireGuard).
const winSize = 2048

type replayWin struct {
	last uint64
	bits [winSize / 64]uint64
}

func (w *replayWin) ok(c uint64) bool {
	if c+winSize <= w.last {
		return false
	}
	if c > w.last {
		return true
	}
	i := c % winSize
	return w.bits[i/64]&(1<<(i%64)) == 0
}

func (w *replayWin) mark(c uint64) {
	if c > w.last {
		if c-w.last >= winSize {
			w.bits = [winSize / 64]uint64{}
		} else {
			for i := w.last + 1; i <= c; i++ {
				j := i % winSize
				w.bits[j/64] &^= 1 << (j % 64)
			}
		}
		w.last = c
	}
	i := c % winSize
	w.bits[i/64] |= 1 << (i % 64)
}

// Сеанс: один набор ключей между двумя узлами.
type session struct {
	ver            int    // версия протокола (protoV1/protoV2)
	id, peerID     uint32 // наш индекс (по нему нас находят) и индекс собеседника (его мы пишем в пакеты)
	send, recv     cipher.AEAD
	sendHP, recvHP [32]byte
	sendCtr        atomic.Uint64
	created        time.Time
	peer           *peer

	mu    sync.Mutex // окно повторов и счётчики приёма
	rwin  replayWin
	rxCnt uint32 // принято пакетов за интервал
	rxMax uint64 // наибольший счётчик
	rxRef uint64 // наибольший счётчик на момент прошлого отчёта
}

func newSession(ver int, id, peerID uint32, sendKey, recvKey, sendHP, recvHP [32]byte, p *peer) *session {
	s := &session{ver: ver, id: id, peerID: peerID, sendHP: sendHP, recvHP: recvHP, created: time.Now(), peer: p}
	s.send, _ = chacha20poly1305.New(sendKey[:])
	s.recv, _ = chacha20poly1305.New(recvKey[:])
	return s
}

// seal шифрует «на месте»: открытый кадр лежит в buf[hdrLen : hdrLen+plen], в итоге buf[:n] — готовый пакет.
func (s *session) seal(buf []byte, plen int) int {
	ctr := s.sendCtr.Add(1) - 1
	var nonce [12]byte
	binary.LittleEndian.PutUint64(nonce[4:], ctr)
	var aad [hdrLen]byte
	binary.BigEndian.PutUint32(aad[0:4], s.peerID)
	binary.BigEndian.PutUint64(aad[4:12], ctr)
	out := s.send.Seal(buf[hdrLen:hdrLen], nonce[:], buf[hdrLen:hdrLen+plen], aad[:])
	n := hdrLen + len(out)
	m := hpMask(s.ver, &s.sendHP, buf[hdrLen:hdrLen+16])
	for i := 0; i < hdrLen; i++ {
		buf[i] = aad[i] ^ m[i]
	}
	return n
}

// peekIndex снимает маску и сверяет индекс — дёшево, расшифровки нет.
func (s *session) peekIndex(p []byte) (hdr [hdrLen]byte, ok bool) {
	if len(p) < hdrLen+minPlain+tagLen {
		return hdr, false
	}
	m := hpMask(s.ver, &s.recvHP, p[hdrLen:hdrLen+16])
	for i := 0; i < hdrLen; i++ {
		hdr[i] = p[i] ^ m[i]
	}
	return hdr, binary.BigEndian.Uint32(hdr[0:4]) == s.id
}

// open расшифровывает «на месте»; возвращает открытый кадр (срез p[hdrLen:...]) и счётчик.
func (s *session) open(p []byte, hdr [hdrLen]byte) ([]byte, uint64, error) {
	ctr := binary.BigEndian.Uint64(hdr[4:12])
	s.mu.Lock()
	ok := s.rwin.ok(ctr)
	s.mu.Unlock()
	if !ok {
		return nil, 0, errReplay
	}
	var nonce [12]byte
	binary.LittleEndian.PutUint64(nonce[4:], ctr)
	plain, err := s.recv.Open(p[hdrLen:hdrLen], nonce[:], p[hdrLen:], hdr[:])
	if err != nil {
		return nil, 0, errAuth
	}
	s.mu.Lock()
	if !s.rwin.ok(ctr) { // другой поток мог принять тот же счётчик
		s.mu.Unlock()
		return nil, 0, errReplay
	}
	s.rwin.mark(ctr)
	s.rxCnt++
	if ctr > s.rxMax {
		s.rxMax = ctr
	}
	s.mu.Unlock()
	return plain, ctr, nil
}
