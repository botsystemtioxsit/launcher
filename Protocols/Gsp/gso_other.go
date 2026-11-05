//go:build !linux

package main

import (
	"net"

	"golang.org/x/net/ipv4"
)

func probeGSO(c *net.UDPConn) bool { return false }

func (b *txBatch) coalesce() []ipv4.Message { return b.msgs }
