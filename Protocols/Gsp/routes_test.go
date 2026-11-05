package main

import (
	"net/netip"
	"testing"
)

func TestTunnelRoutesSplitDefault(t *testing.T) {
	got := tunnelRoutes([]netip.Prefix{netip.MustParsePrefix("0.0.0.0/0"), netip.MustParsePrefix("::/0"), netip.MustParsePrefix("10.77.0.1/32")})
	want := []string{"0.0.0.0/1", "128.0.0.0/1", "::/1", "8000::/1", "10.77.0.1/32"}
	if len(got) != len(want) {
		t.Fatalf("маршрутов %d, ожидалось %d: %v", len(got), len(want), got)
	}
	for i, w := range want {
		if got[i].String() != w {
			t.Fatalf("маршрут %d: %s, ожидался %s", i, got[i], w)
		}
	}
}

func TestEndpointRouteNeeded(t *testing.T) {
	full := []netip.Prefix{netip.MustParsePrefix("0.0.0.0/0")}
	if !needsEndpointRoute(netip.MustParseAddr("203.0.113.5"), full) {
		t.Fatal("при «всё в туннель» сервер надо вывести из туннеля")
	}
	if needsEndpointRoute(netip.MustParseAddr("127.0.0.1"), full) {
		t.Fatal("петлевой адрес не надо")
	}
	only := []netip.Prefix{netip.MustParsePrefix("10.77.0.1/32")}
	if needsEndpointRoute(netip.MustParseAddr("203.0.113.5"), only) {
		t.Fatal("сервер вне маршрутов шлюза — отдельный маршрут не нужен")
	}
	if !needsEndpointRoute(netip.MustParseAddr("::ffff:203.0.113.5"), full) {
		t.Fatal("адрес в виде ::ffff:… надо приводить к обычному")
	}
}
