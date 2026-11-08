//go:build windows

package main

import (
	"net/netip"

	"golang.zx2c4.com/wireguard/windows/tunnel/firewall"
)

// enableKillSwitch включает правила WFP (код из wireguard-windows, лицензия MIT, подключён как библиотека).
// luid — сетевая карта шлюза: трафик на ней разрешён; dns — серверы имён, к которым разрешены запросы DNS.
// Возвращает функцию, которая правила снимает.
func enableKillSwitch(luid uint64, dns []netip.Addr) (func(), error) {
	if err := firewall.EnableFirewall(luid, false, dns); err != nil {
		return nil, err
	}
	return firewall.DisableFirewall, nil
}
