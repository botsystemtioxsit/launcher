package main

import (
	"net"

	"golang.org/x/net/ipv4"
)

// batchIO — чтение и запись пачками. На Linux это recvmmsg/sendmmsg (быстро), на остальных системах
// и по требованию (GSP_PLAINIO=1) — обычное чтение и запись по одному пакету: оно работает везде одинаково.
type batchIO interface {
	ReadBatch(ms []ipv4.Message, flags int) (int, error)
	WriteBatch(ms []ipv4.Message, flags int) (int, error)
}

type plainIO struct{ c *net.UDPConn }

func (p plainIO) ReadBatch(ms []ipv4.Message, _ int) (int, error) {
	n, addr, err := p.c.ReadFromUDP(ms[0].Buffers[0])
	if err != nil {
		return 0, err
	}
	ms[0].N = n
	ms[0].Addr = addr
	return 1, nil
}

func (p plainIO) WriteBatch(ms []ipv4.Message, _ int) (int, error) {
	for i := range ms {
		a, _ := ms[i].Addr.(*net.UDPAddr)
		if a == nil {
			continue
		}
		for _, b := range ms[i].Buffers {
			if _, err := p.c.WriteToUDP(b, a); err != nil {
				return i, err
			}
		}
	}
	return len(ms), nil
}
