//go:build !windows

package main

import "net/netip"

// Защита от утечек сделана только для Windows-клиента; на сервере (Linux) она не нужна.
func enableKillSwitch(luid uint64, dns []netip.Addr) (func(), error) { return func() {}, nil }
