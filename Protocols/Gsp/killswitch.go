package main

// Защита от утечек («kill switch») — решение, нужна ли она. Сама защита — только на Windows (killswitch_windows.go):
// брандмауэр Windows (платформа WFP) на время работы службы запрещает весь трафик вне сетевой карты шлюза, кроме самой
// программы gsp (она шлёт зашифрованные пакеты серверу), DHCP, локальной петли и NDP. Заодно запрещены запросы DNS не к
// серверам из настроек (утечка DNS) и весь IPv6 вне туннеля (утечка IPv6). Правила живут, пока жива служба: корректная остановка
// их снимает и возвращает интернет; если службу убили жёстко, Windows убирает правила вместе с ней (так же работает WireGuard).
// Побочное действие: пока защита включена, локальная сеть (принтеры, NAS) недоступна.

import (
	"net/netip"
	"sync/atomic"
)

// killSwitchOn — защита сейчас включена (попадает в файл состояния, чтобы окно плагина показало её словами).
var killSwitchOn atomic.Bool

// fallbackDNS — на случай, если в настройках нет DNS: при включённой защите без него имена не открывались бы вовсе
// (запросы к DNS провайдера идут мимо туннеля и блокируются).
var fallbackDNS = []netip.Addr{netip.MustParseAddr("1.1.1.1"), netip.MustParseAddr("9.9.9.9")}

// wantKillSwitch: нужна ли защита при таких настройках.
//
//	on  — всегда;  off — никогда;
//	auto — как у WireGuard: когда в туннель идёт весь интернет, то есть в AllowedIPs есть точный 0.0.0.0/0 или ::/0.
//	Если часть сетей отправлена мимо шлюза (список AllowedIPs «всё, кроме…»), «auto» защиту не включает: иначе исключённые сети были бы заблокированы.
func wantKillSwitch(fc *FileConfig) bool {
	switch fc.KillSwitch {
	case "on":
		return true
	case "off":
		return false
	}
	for _, p := range fc.Engine.Peers {
		for _, a := range p.Allowed {
			if a.Bits() == 0 && a == a.Masked() {
				return true
			}
		}
	}
	return false
}
