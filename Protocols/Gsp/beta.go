package main

// «GSP Beta» (версия протокола 3) — тестовая версия, нацеленная на сети с глубоким анализом трафика (в том числе ТСПУ).
//
// Что меняется по сравнению с версией 2 (шифрование и обмен ключами — те же проверенные, своей криптографии нет):
//  1. Рукопожатие больше не «просто случайные байты»: пакет начинается как длинный заголовок QUIC Initial
//     (флаги, версия 1, идентификаторы соединения, длина) и добирается до 1200–1300 байт, как у настоящих QUIC-клиентов.
//     Внутри — то же упакованное рукопожатие. Это ПОХОЖЕСТЬ ФОРМЫ, а не настоящий QUIC: тот, кто расшифрует «начальный пакет» по правилам QUIC, увидит, что это не QUIC.
//  2. Перед первым рукопожатием клиент шлёт несколько «мусорных» пакетов случайной длины (сервер их молча отбрасывает), чтобы начало обмена не имело постоянной формы.
//  3. К пакетам данных по умолчанию добавляется случайная набивка (до betaPad байт), чтобы размеры не выдавали содержимое.
//
// Что НЕ сделано: запасной режим через TCP/443, смена портов, проверка на настоящем ТСПУ.

import (
	"net"
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

// betaPadFor — сколько набивки добавить в рукопожатие длиной msgLen, чтобы вся датаграмма вышла размером betaMinTotal..betaMaxTotal.
func betaPadFor(msgLen int) int {
	fixed := betaHdrLen + 24 + 2 + msgLen + tagLen
	p := randIn(betaMinTotal, betaMaxTotal) - fixed
	if p < 0 {
		p = 0
	}
	return p
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
