//go:build !linux

package main

import "net"

func newBatchIO(c *net.UDPConn) batchIO { return plainIO{c} }
