package main

import "net/netip"

// Маршруты, которые клиент прописывает на сетевой карте шлюза. «Всё в туннель» (0.0.0.0/0) делится на две половины
// (0.0.0.0/1 и 128.0.0.0/1): они точнее обычного маршрута по умолчанию, поэтому перебивают его, и при этом сам он не меняется —
// так делает и WireGuard.
func tunnelRoutes(allowed []netip.Prefix) []netip.Prefix {
	var out []netip.Prefix
	seen := map[netip.Prefix]bool{}
	add := func(p netip.Prefix) {
		p = p.Masked()
		if !seen[p] {
			seen[p] = true
			out = append(out, p)
		}
	}
	for _, p := range allowed {
		if p.Bits() == 0 {
			if p.Addr().Is4() {
				add(netip.MustParsePrefix("0.0.0.0/1"))
				add(netip.MustParsePrefix("128.0.0.0/1"))
			} else {
				add(netip.MustParsePrefix("::/1"))
				add(netip.MustParsePrefix("8000::/1"))
			}
			continue
		}
		add(p)
	}
	return out
}

// Нужен ли отдельный маршрут к серверу мимо туннеля: да, если адрес сервера попал бы в маршруты шлюза
// (иначе сами пакеты шлюза пошли бы в шлюз — петля). Петлевые адреса через шлюз не ходят никогда.
func needsEndpointRoute(ep netip.Addr, allowed []netip.Prefix) bool {
	ep = ep.Unmap()
	if !ep.IsValid() || ep.IsLoopback() {
		return false
	}
	for _, p := range tunnelRoutes(allowed) {
		if p.Contains(ep) {
			return true
		}
	}
	return false
}
