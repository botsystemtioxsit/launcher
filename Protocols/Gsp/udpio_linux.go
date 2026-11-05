//go:build linux

package main

import (
	"net"
	"os"

	"golang.org/x/net/ipv4"
)

func newBatchIO(c *net.UDPConn) batchIO {
	if os.Getenv("GSP_PLAINIO") != "" {
		return plainIO{c}
	}
	return ipv4.NewPacketConn(c)
}
