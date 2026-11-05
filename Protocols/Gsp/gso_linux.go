//go:build linux

package main

import (
	"encoding/binary"
	"net"
	"unsafe"

	"golang.org/x/net/ipv4"
	"golang.org/x/sys/unix"
)

// UDP GSO (UDP_SEGMENT): ядру отдаётся одна «длинная» посылка из десятков одинаковых пакетов,
// а резать её на части оно будет само — и как можно позже. Это сильно дешевле, чем по системному вызову на пакет.
func probeGSO(c *net.UDPConn) bool {
	rc, err := c.SyscallConn()
	if err != nil {
		return false
	}
	ok := false
	_ = rc.Control(func(fd uintptr) {
		_, err := unix.GetsockoptInt(int(fd), unix.IPPROTO_UDP, unix.UDP_SEGMENT)
		ok = err == nil
	})
	return ok
}

const gsoMaxSegs = 64

var gsoSpace = unix.CmsgSpace(2)

func setGSOSize(oob []byte, size uint16) []byte {
	oob = oob[:gsoSpace]
	h := (*unix.Cmsghdr)(unsafe.Pointer(&oob[0]))
	h.Level = unix.IPPROTO_UDP
	h.Type = unix.UDP_SEGMENT
	h.SetLen(unix.CmsgLen(2))
	binary.NativeEndian.PutUint16(oob[unix.CmsgLen(0):], size)
	return oob
}

// coalesce склеивает подряд идущие пакеты одного размера и одному адресату в одну посылку.
func (b *txBatch) coalesce() []ipv4.Message {
	if cap(b.gmsgs) < len(b.msgs) {
		b.gmsgs = make([]ipv4.Message, 0, len(b.msgs))
		b.goob = make([]byte, len(b.msgs)*gsoSpace)
		b.giov = make([][]byte, 0, len(b.msgs))
	}
	b.gmsgs, b.giov = b.gmsgs[:0], b.giov[:0]
	oobUsed := 0
	i := 0
	for i < len(b.msgs) {
		m := b.msgs[i]
		sz := len(m.Buffers[0])
		j, total := i+1, sz
		for j < len(b.msgs) && j-i < gsoMaxSegs && b.msgs[j].Addr == m.Addr {
			l := len(b.msgs[j].Buffers[0])
			if l > sz || total+l > 65000 {
				break
			}
			total += l
			j++
			if l < sz {
				break // короткий пакет может быть только последним
			}
		}
		if j-i == 1 {
			b.gmsgs = append(b.gmsgs, m)
		} else {
			start := len(b.giov)
			for k := i; k < j; k++ {
				b.giov = append(b.giov, b.msgs[k].Buffers[0])
			}
			oob := setGSOSize(b.goob[oobUsed:oobUsed+gsoSpace], uint16(sz))
			oobUsed += gsoSpace
			b.gmsgs = append(b.gmsgs, ipv4.Message{Buffers: b.giov[start:len(b.giov)], Addr: m.Addr, OOB: oob})
		}
		i = j
	}
	return b.gmsgs
}
