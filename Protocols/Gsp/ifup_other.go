//go:build !linux && !windows

package main

import "golang.zx2c4.com/wireguard/tun"

func setupNetwork(dev tun.Device, name string, fc *FileConfig) (func(), error) {
	return func() {}, nil
}
