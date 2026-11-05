// relay — прокладка для проверок: пересылает UDP между клиентом и сервером,
// теряя долю пакетов и добавляя задержку. Одинаково работает для любого протокола (GSP, WireGuard…).
package main

import (
	"flag"
	"fmt"
	"math/rand"
	"net"
	"sync"
	"time"
)

func main() {
	listen := flag.String("l", "", "адрес:порт для клиента")
	server := flag.String("s", "", "адрес:порт сервера")
	bind := flag.String("b", "", "свой адрес со стороны сервера")
	loss := flag.Float64("loss", 0, "доля потерь, %")
	delay := flag.Duration("delay", 0, "односторонняя задержка")
	flag.Parse()
	la, err := net.ResolveUDPAddr("udp4", *listen)
	must(err)
	sa, err := net.ResolveUDPAddr("udp4", *server)
	must(err)
	ba := &net.UDPAddr{IP: net.ParseIP(*bind)}
	cc, err := net.ListenUDP("udp4", la)
	must(err)
	sc, err := net.ListenUDP("udp4", ba)
	must(err)
	_ = cc.SetReadBuffer(8 << 20)
	_ = sc.SetReadBuffer(8 << 20)
	_ = cc.SetWriteBuffer(8 << 20)
	_ = sc.SetWriteBuffer(8 << 20)
	var cli *net.UDPAddr
	var mu sync.Mutex
	rng := rand.New(rand.NewSource(time.Now().UnixNano()))
	lost := func() bool {
		mu.Lock()
		defer mu.Unlock()
		return rng.Float64()*100 < *loss
	}
	send := func(c *net.UDPConn, to *net.UDPAddr, b []byte) {
		if *delay > 0 {
			time.AfterFunc(*delay, func() { c.WriteToUDP(b, to) })
		} else {
			c.WriteToUDP(b, to)
		}
	}
	go func() {
		buf := make([]byte, 65536)
		for {
			n, a, err := cc.ReadFromUDP(buf)
			if err != nil {
				return
			}
			mu.Lock()
			cli = a
			mu.Unlock()
			if lost() {
				continue
			}
			send(sc, sa, append([]byte(nil), buf[:n]...))
		}
	}()
	buf := make([]byte, 65536)
	fmt.Printf("relay %s -> %s, потери %.1f%%, задержка %v\n", *listen, *server, *loss, *delay)
	for {
		n, _, err := sc.ReadFromUDP(buf)
		if err != nil {
			return
		}
		mu.Lock()
		a := cli
		mu.Unlock()
		if a == nil || lost() {
			continue
		}
		send(cc, a, append([]byte(nil), buf[:n]...))
	}
}

func must(err error) {
	if err != nil {
		panic(err)
	}
}
