//go:build linux

package main

import (
	"fmt"
	"os/exec"
)

// На Linux адрес, размер пакета и «поднять» делаем командой ip. Маршруты и NAT — дело скриптов установки.
func configureInterface(name string, fc *FileConfig) error {
	run := func(args ...string) error {
		out, err := exec.Command("ip", args...).CombinedOutput()
		if err != nil {
			return fmt.Errorf("ip %v: %v: %s", args, err, out)
		}
		return nil
	}
	for _, a := range fc.Address {
		if err := run("addr", "add", a.String(), "dev", name); err != nil {
			return err
		}
	}
	return run("link", "set", "dev", name, "mtu", fmt.Sprint(fc.MTU), "up")
}
