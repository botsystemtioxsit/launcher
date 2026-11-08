//go:build windows

package main

import (
	"fmt"
	"net/netip"
	"sort"
	"sync"
	"time"

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
		er := newEndpointRoute(luid, p.Endpoint.Addr().Unmap())
		if _, err := er.ensure(); err != nil {
			return fail(fmt.Errorf("маршрут к серверу мимо туннеля: %w", err))
		}
		er.watch(2*time.Second, func() { networkChanged() })
		undo = append(undo, er.remove)
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

	// защита от утечек: без DNS в настройках имена не открылись бы (запросы к DNS провайдера уходят мимо туннеля и блокируются)
	ks := wantKillSwitch(fc)
	if ks && len(fc.DNS) == 0 {
		fc.DNS = append([]netip.Addr(nil), fallbackDNS...)
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

	// защита от утечек — последней, когда адрес, маршруты и DNS уже на месте (при остановке снимается первой)
	if ks {
		off, err := enableKillSwitch(uint64(luid), fc.DNS)
		if err != nil {
			return fail(fmt.Errorf("не удалось включить защиту от утечек: %w", err))
		}
		killSwitchOn.Store(true)
		undo = append(undo, func() { killSwitchOn.Store(false); off() })
	}
	return cleanup, nil
}

// endpointRoute — узкий маршрут /32 к серверу мимо туннеля. Находит, как сейчас Windows добирается до сервера (маршрут не через нашу карту),
// и ставит точно такой же, чтобы более широкие маршруты шлюза его не перебили. Следит за сменой сети (Wi-Fi → кабель → точка доступа):
// Windows убирает маршруты пропавшей карты, и без нового маршрута зашифрованные пакеты шлюза пошли бы в сам шлюз, то есть связь умерла бы
// до ручного переподключения. Поэтому раз в несколько секунд маршрут проверяется и при необходимости переставляется.
type endpointRoute struct {
	mu   sync.Mutex
	ours winipcfg.LUID
	ep   netip.Addr
	host netip.Prefix
	cur  *routeCand
	stop chan struct{}
	done chan struct{}
}

type routeCand struct {
	bits int
	cost uint32
	luid winipcfg.LUID
	next netip.Addr
}

func (c routeCand) same(o routeCand) bool { return c.luid == o.luid && c.next == o.next }

// pickUnderlay выбирает лучший обычный маршрут до ep из таблицы (не через нашу карту и не наш собственный узкий маршрут).
func pickUnderlay(rows []winipcfg.MibIPforwardRow2, ours winipcfg.LUID, ep netip.Addr, host netip.Prefix, fam winipcfg.AddressFamily) (routeCand, bool) {
	var cs []routeCand
	for i := range rows {
		r := &rows[i]
		if r.InterfaceLUID == ours {
			continue
		}
		p := r.DestinationPrefix.Prefix()
		if !p.IsValid() || !p.Contains(ep) || p == host {
			continue
		}
		cost := r.Metric
		if ifr, err := r.InterfaceLUID.IPInterface(fam); err == nil {
			cost += ifr.Metric
		}
		cs = append(cs, routeCand{bits: p.Bits(), cost: cost, luid: r.InterfaceLUID, next: r.NextHop.Addr()})
	}
	if len(cs) == 0 {
		return routeCand{}, false
	}
	sort.Slice(cs, func(i, j int) bool {
		if cs[i].bits != cs[j].bits {
			return cs[i].bits > cs[j].bits
		}
		return cs[i].cost < cs[j].cost
	})
	return cs[0], true
}

func epFamily(ep netip.Addr) winipcfg.AddressFamily {
	if ep.Is6() {
		return windows.AF_INET6
	}
	return windows.AF_INET
}

// ensure приводит маршрут в порядок. changed — маршрут был поставлен заново (сеть сменилась).
func (r *endpointRoute) ensure() (changed bool, err error) {
	r.mu.Lock()
	defer r.mu.Unlock()
	fam := epFamily(r.ep)
	rows, err := winipcfg.GetIPForwardTable2(fam)
	if err != nil {
		return false, err
	}
	best, ok := pickUnderlay(rows, r.ours, r.ep, r.host, fam)
	if !ok {
		return false, fmt.Errorf("нет обычного маршрута до %s", r.ep)
	}
	if r.cur != nil && r.cur.same(best) {
		for i := range rows { // наш маршрут на месте?
			row := &rows[i]
			if row.InterfaceLUID == best.luid && row.DestinationPrefix.Prefix() == r.host && row.NextHop.Addr() == best.next {
				return false, nil
			}
		}
	}
	if r.cur != nil {
		_ = r.cur.luid.DeleteRoute(r.host, r.cur.next) // прежний маршрут (его карта могла исчезнуть — тогда ошибка безвредна)
	}
	_ = best.luid.DeleteRoute(r.host, best.next) // след прошлого запуска, если был
	if err := best.luid.AddRoute(r.host, best.next, 1); err != nil {
		r.cur = nil
		return false, err
	}
	changed = r.cur != nil
	b := best
	r.cur = &b
	return changed, nil
}

// remove останавливает слежение и убирает маршрут.
func (r *endpointRoute) remove() {
	if r.stop != nil {
		close(r.stop)
		<-r.done
		r.stop = nil
	}
	r.mu.Lock()
	defer r.mu.Unlock()
	if r.cur != nil {
		_ = r.cur.luid.DeleteRoute(r.host, r.cur.next)
		r.cur = nil
	}
}

// watch раз в interval проверяет маршрут; onChange вызывается, когда он переставлен из-за смены сети.
func (r *endpointRoute) watch(interval time.Duration, onChange func()) {
	r.stop = make(chan struct{})
	r.done = make(chan struct{})
	go func() {
		defer close(r.done)
		t := time.NewTicker(interval)
		defer t.Stop()
		for {
			select {
			case <-r.stop:
				return
			case <-t.C:
				if changed, err := r.ensure(); err == nil && changed && onChange != nil {
					onChange()
				}
			}
		}
	}()
}

// addEndpointRoute ставит маршрут один раз (без слежения); возвращает функцию, которая его убирает.
func addEndpointRoute(ours winipcfg.LUID, ep netip.Addr) (func(), error) {
	r := newEndpointRoute(ours, ep)
	if _, err := r.ensure(); err != nil {
		return nil, err
	}
	return r.remove, nil
}

func newEndpointRoute(ours winipcfg.LUID, ep netip.Addr) *endpointRoute {
	return &endpointRoute{ours: ours, ep: ep, host: netip.PrefixFrom(ep, ep.BitLen())}
}
