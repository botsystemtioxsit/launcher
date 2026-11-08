package main

// Ограничитель рукопожатий (защита сервера от перегрузки).
//
// Разбор init-пакета (Noise IK) требует двух умножений на кривой Curve25519 — это самое дорогое, что сервер делает
// по пакету «из интернета». Тот, кто знает открытый ключ сервера, может сыпать пакетами с правильной внешней упаковкой
// и пустым содержимым: каждый заставит сервер считать. Поэтому до разбора init проверяется ограничитель:
//   - на один адрес-источник — не больше perIPRate рукопожатий в секунду (с запасом perIPBurst: сеть моргнула, клиент повторил);
//   - на весь сервер — не больше globalRate в секунду (с запасом globalBurst), против рассылки с подменёнными адресами.
// Лишние пакеты молча отбрасываются (как и любые чужие). Для личного сервера пределов хватает с большим запасом:
// настоящий клиент делает рукопожатие раз в две минуты и повторяет не чаще раза в три секунды.
// Памяти ограничитель берёт не больше maxTracked записей: при переполнении старые убираются, а новые адреса
// проверяются только общим пределом.

import (
	"net/netip"
	"sync"
	"time"
)

const (
	perIPRate    = 2.0   // рукопожатий в секунду с одного адреса
	perIPBurst   = 10.0  // …и сколько можно подряд
	globalRate   = 100.0 // всего в секунду
	globalBurst  = 200.0
	maxTracked   = 8192
	trackedTTL   = time.Minute
	limiterGCGap = 10 * time.Second
)

type bucket struct {
	tokens float64
	last   time.Time
}

func (b *bucket) take(now time.Time, rate, burst float64) bool {
	if b.last.IsZero() {
		b.tokens = burst
	} else if d := now.Sub(b.last).Seconds(); d > 0 {
		b.tokens += d * rate
		if b.tokens > burst {
			b.tokens = burst
		}
	}
	b.last = now
	if b.tokens < 1 {
		return false
	}
	b.tokens--
	return true
}

type hsLimiter struct {
	mu     sync.Mutex
	global bucket
	perIP  map[netip.Addr]*bucket
	lastGC time.Time
	// пределы вынесены в поля, чтобы тест мог их менять
	ipRate, ipBurst, gRate, gBurst float64
}

func newHSLimiter() *hsLimiter {
	return &hsLimiter{perIP: map[netip.Addr]*bucket{}, ipRate: perIPRate, ipBurst: perIPBurst, gRate: globalRate, gBurst: globalBurst}
}

// allow: можно ли разбирать рукопожатие, пришедшее с адреса ip.
func (l *hsLimiter) allow(ip netip.Addr, now time.Time) bool {
	l.mu.Lock()
	defer l.mu.Unlock()
	if now.Sub(l.lastGC) > limiterGCGap {
		l.gc(now)
	}
	// сначала предел адреса: рассылка с одного адреса не должна выедать общий предел
	b := l.perIP[ip]
	if b == nil && len(l.perIP) >= maxTracked {
		l.gc(now)
	}
	if b == nil && len(l.perIP) < maxTracked {
		b = &bucket{}
		l.perIP[ip] = b
	}
	if b != nil && !b.take(now, l.ipRate, l.ipBurst) {
		return false
	}
	// таблица полна (рассылка с подменёнными адресами) — b == nil: пускаем только по общему пределу
	return l.global.take(now, l.gRate, l.gBurst)
}

func (l *hsLimiter) gc(now time.Time) {
	l.lastGC = now
	for ip, b := range l.perIP {
		if now.Sub(b.last) > trackedTTL {
			delete(l.perIP, ip)
		}
	}
}
