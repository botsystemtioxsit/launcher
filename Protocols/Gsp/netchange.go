package main

import "sync/atomic"

// netChangeHook вызывается, когда клиент заметил смену сети (Windows: переставлен маршрут к серверу). Ставит его startNode.
var netChangeHook atomic.Pointer[func()]

func networkChanged() {
	if f := netChangeHook.Load(); f != nil {
		(*f)()
	}
}
