//go:build windows

package main

import (
	"fmt"
	"net/netip"
	"sort"

	"golang.org/x/sys/windows"
	"golang.zx2c4.com/wireguard/tun"
	"golang.zx2c4.com/wireguard/windows/tunnel/winipcfg"
)

func init() {
	tun.WintunTunnelType = "Ghost Shell"
}

// Windows: адрес, размер пакета, метрика, маршруты и DNS на сетевой карте шлюза.
// Возвращает функцию, которая убирает всё, что мы поставили вне самой карты (маршрут к серверу мимо туннеля).
func setupNetwork(dev tun.Device, name string, fc *FileConfig) (func(), error) {
	nt, ok := dev.(*tun.NativeTun)
	if !ok {
		return nil, fmt.Errorf("неожиданный тип сетевой карты %T", dev)
	}
	luid := winipcfg.LUID(nt.LUID())
	var undo []func()
	cleanup := func() {
		for i := len(undo) - 1; i >= 0; i-- {
			undo[i]()
		}
	}
	fail := func(err error) (func(), error) { cleanup(); return nil, err }

	// размер пакета и метрика: у шлюза она самая маленькая, чтобы Windows предпочитала его карту (в том числе для DNS)
	for _, fam := range []winipcfg.AddressFamily{windows.AF_INET, windows.AF_INET6} {
		row, err := luid.IPInterface(fam)
		if err != nil {
			if fam == windows.AF_INET {
				return fail(fmt.Errorf("IPv4 на сетевой карте шлюза: %w", err))
			}
			continue // IPv6 в системе может быть выключен
		}
		row.NLMTU = uint32(fc.MTU)
		row.UseAutomaticMetric = false
		row.Metric = 0
		row.DadTransmits = 0
		row.RouterDiscoveryBehavior = winipcfg.RouterDiscoveryDisabled
		if err := row.Set(); err != nil && fam == windows.AF_INET {
			return fail(fmt.Errorf("размер пакета и метрика: %w", err))
		}
	}

	if len(fc.Address) > 0 {
		if err := luid.SetIPAddresses(fc.Address); err != nil {
			return fail(fmt.Errorf("адрес на сетевой карте: %w", err))
		}
	}

	// сервер должен быть доступен мимо туннеля, иначе пакеты шлюза пойдут в шлюз
	var allowed []netip.Prefix
	for _, p := range fc.Engine.Peers {
		allowed = append(allowed, p.Allowed...)
	}
	for _, p := range fc.Engine.Peers {
		if !p.Endpoint.IsValid() || !needsEndpointRoute(p.Endpoint.Addr(), allowed) {
			continue
		}
		del, err := addEndpointRoute(luid, p.Endpoint.Addr().Unmap())
		if err != nil {
			return fail(fmt.Errorf("маршрут к серверу мимо туннеля: %w", err))
		}
		undo = append(undo, del)
	}

	// маршруты шлюза
	for _, r := range tunnelRoutes(allowed) {
		next := netip.IPv4Unspecified()
		if r.Addr().Is6() {
			next = netip.IPv6Unspecified()
		}
		if err := luid.AddRoute(r, next, 0); err != nil {
			if r.Addr().Is6() {
				continue // IPv6 выключен — пропускаем
			}
			return fail(fmt.Errorf("маршрут %s: %w", r, err))
		}
	}

	// DNS
	var v4, v6 []netip.Addr
	for _, d := range fc.DNS {
		if d.Is4() {
			v4 = append(v4, d)
		} else {
			v6 = append(v6, d)
		}
	}
	if len(v4) > 0 {
		if err := luid.SetDNS(windows.AF_INET, v4, nil); err != nil {
			return fail(fmt.Errorf("DNS: %w", err))
		}
	}
	if len(v6) > 0 {
		_ = luid.SetDNS(windows.AF_INET6, v6, nil)
	}
	return cleanup, nil
}

// Находит, как сейчас Windows добирается до адреса (маршрут не через нашу карту), и ставит точно такой же узкий маршрут /32,
// чтобы потом более широкие маршруты шлюза его не перебили. Возвращает функцию, которая этот маршрут убирает.
func addEndpointRoute(ours winipcfg.LUID, ep netip.Addr) (func(), error) {
	fam := winipcfg.AddressFamily(windows.AF_INET)
	if ep.Is6() {
		fam = windows.AF_INET6
	}
	rows, err := winipcfg.GetIPForwardTable2(fam)
	if err != nil {
		return nil, err
	}
	type cand struct {
		bits   int
		cost   uint32
		luid   winipcfg.LUID
		next   netip.Addr
		prefix netip.Prefix
	}
	var cs []cand
	for _, r := range rows {
		if r.InterfaceLUID == ours {
			continue
		}
		p := r.DestinationPrefix.Prefix()
		if !p.IsValid() || !p.Contains(ep) {
			continue
		}
		cost := r.Metric
		if ifr, err := r.InterfaceLUID.IPInterface(fam); err == nil {
			cost += ifr.Metric
		}
		cs = append(cs, cand{bits: p.Bits(), cost: cost, luid: r.InterfaceLUID, next: r.NextHop.Addr(), prefix: p})
	}
	if len(cs) == 0 {
		return nil, fmt.Errorf("нет обычного маршрута до %s", ep)
	}
	sort.Slice(cs, func(i, j int) bool {
		if cs[i].bits != cs[j].bits {
			return cs[i].bits > cs[j].bits
		}
		return cs[i].cost < cs[j].cost
	})
	best := cs[0]
	host := netip.PrefixFrom(ep, ep.BitLen())
	_ = best.luid.DeleteRoute(host, best.next) // след прошлого запуска, если был
	if err := best.luid.AddRoute(host, best.next, 1); err != nil {
		return nil, err
	}
	return func() { _ = best.luid.DeleteRoute(host, best.next) }, nil
}
