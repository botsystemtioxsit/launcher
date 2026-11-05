package main

import (
	"encoding/binary"
	"errors"
	"sync"
)

// echoDevice — «сетевая карта» без сети: отвечает на ping (ICMP echo) любому адресу. Нужна для проверок:
// на Windows-машине GitHub нет второго компьютера, поэтому сервер там — этот ответчик, а клиент настоящий.
type echoDevice struct {
	in     chan []byte
	closed chan struct{}
	once   sync.Once
}

func newEchoDevice() *echoDevice {
	return &echoDevice{in: make(chan []byte, 1024), closed: make(chan struct{})}
}

func (d *echoDevice) Read(bufs [][]byte, sizes []int, offset int) (int, error) {
	select {
	case p := <-d.in:
		copy(bufs[0][offset:], p)
		sizes[0] = len(p)
		return 1, nil
	case <-d.closed:
		return 0, errors.New("closed")
	}
}

func (d *echoDevice) Write(bufs [][]byte, offset int) (int, error) {
	for _, b := range bufs {
		if r := icmpEchoReply(b[offset:]); r != nil {
			select {
			case d.in <- r:
			default:
			}
		}
	}
	return len(bufs), nil
}

func (d *echoDevice) BatchSize() int { return 1 }
func (d *echoDevice) Close() error   { d.once.Do(func() { close(d.closed) }); return nil }

func inetChecksum(b []byte) uint16 {
	var s uint32
	for i := 0; i+1 < len(b); i += 2 {
		s += uint32(binary.BigEndian.Uint16(b[i:]))
	}
	if len(b)%2 == 1 {
		s += uint32(b[len(b)-1]) << 8
	}
	for s>>16 != 0 {
		s = s&0xffff + s>>16
	}
	return ^uint16(s)
}

// Ответ на ICMP echo request (IPv4); для всего остального nil.
func icmpEchoReply(pkt []byte) []byte {
	if len(pkt) < 28 || pkt[0]>>4 != 4 || pkt[9] != 1 {
		return nil
	}
	ihl := int(pkt[0]&15) * 4
	if ihl < 20 || len(pkt) < ihl+8 || pkt[ihl] != 8 {
		return nil
	}
	r := append([]byte(nil), pkt...)
	copy(r[12:16], pkt[16:20])
	copy(r[16:20], pkt[12:16])
	r[8] = 64
	r[ihl] = 0
	r[ihl+2], r[ihl+3] = 0, 0
	binary.BigEndian.PutUint16(r[ihl+2:], inetChecksum(r[ihl:]))
	r[10], r[11] = 0, 0
	binary.BigEndian.PutUint16(r[10:], inetChecksum(r[:ihl]))
	return r
}
