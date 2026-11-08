package main

import (
	"bufio"
	"encoding/json"
	"flag"
	"fmt"
	"net/netip"
	"os"
	"os/signal"
	"runtime/pprof"
	"strings"
	"syscall"
	"time"

	"golang.zx2c4.com/wireguard/tun"
)

const version = "0.2.0"

func usage() {
	fmt.Fprintln(os.Stderr, `gsp — Ghost Shell Protocol `+version+`
  gsp genkey                                  новый закрытый ключ
  gsp pubkey                                  открытый ключ по закрытому (читает со входа)
  gsp run -c файл [-status файл] [-v]         запустить (сервер или клиент — по настройкам)
  gsp echo-server -key К -peer К -allowed Сеть  сервер-ответчик на ping (для проверок, без сетевой карты)
  gsp install-service -c файл -name Имя [-status файл]   Windows: поставить и запустить службу шлюза
  gsp remove-service -name Имя                           Windows: остановить и убрать службу
  gsp service-state -name Имя                            Windows: running | stopped | none
  gsp selftest-route -ip АДРЕС [-hold СЕК] [-watch]       Windows, для проверок: поставить и убрать маршрут к серверу мимо туннеля (с -watch — ещё и следить за ним)
  gsp version`)
	os.Exit(2)
}

func main() {
	if len(os.Args) < 2 {
		usage()
	}
	switch os.Args[1] {
	case "version":
		fmt.Println("gsp", version)
	case "genkey":
		k, err := newKeypair()
		if err != nil {
			fatal(err)
		}
		fmt.Println(keyToB64(k.Private))
	case "pubkey":
		line, _ := bufio.NewReader(os.Stdin).ReadString('\n')
		priv, err := keyFromB64(strings.TrimSpace(line))
		if err != nil {
			fatal(err)
		}
		pub, err := pubFromPriv(priv)
		if err != nil {
			fatal(err)
		}
		fmt.Println(keyToB64(pub))
	case "run", "server", "client":
		fs := flag.NewFlagSet("run", flag.ExitOnError)
		conf := fs.String("c", "", "файл настроек")
		status := fs.String("status", "", "файл, куда раз в секунду пишется состояние (JSON)")
		verbose := fs.Bool("v", false, "подробный журнал")
		_ = fs.Parse(os.Args[2:])
		if *conf == "" {
			usage()
		}
		stop, err := startNode(*conf, *status, *verbose)
		if err != nil {
			fatal(err)
		}
		sig := make(chan os.Signal, 1)
		signal.Notify(sig, os.Interrupt, syscall.SIGTERM)
		<-sig
		stop()
	case "echo-server":
		fs := flag.NewFlagSet("echo-server", flag.ExitOnError)
		key := fs.String("key", "", "закрытый ключ сервера (base64)")
		peerKey := fs.String("peer", "", "открытый ключ клиента (base64)")
		allowed := fs.String("allowed", "10.77.0.2/32", "какие адреса разрешены клиенту")
		port := fs.Int("port", 0, "порт (0 — любой)")
		_ = fs.Parse(os.Args[2:])
		if err := runEchoServer(*key, *peerKey, *allowed, *port); err != nil {
			fatal(err)
		}
	case "selftest-route":
		fs := flag.NewFlagSet("selftest-route", flag.ExitOnError)
		ip := fs.String("ip", "", "адрес, к которому ставим узкий маршрут")
		hold := fs.Int("hold", 5, "сколько секунд держать маршрут")
		watch := fs.Bool("watch", false, "следить за маршрутом и переставлять его, если Windows его убрала (как при смене сети)")
		_ = fs.Parse(os.Args[2:])
		if err := selftestRoute(*ip, *hold, *watch); err != nil {
			fatal(err)
		}
	case "winservice":
		fs := flag.NewFlagSet("winservice", flag.ExitOnError)
		conf := fs.String("c", "", "файл настроек")
		name := fs.String("name", "", "имя шлюза")
		status := fs.String("status", "", "файл состояния")
		_ = fs.Parse(os.Args[2:])
		if err := runWinService(*conf, *name, *status); err != nil {
			fatal(err)
		}
	case "install-service":
		fs := flag.NewFlagSet("install-service", flag.ExitOnError)
		conf := fs.String("c", "", "файл настроек")
		name := fs.String("name", "", "имя шлюза")
		status := fs.String("status", "", "файл состояния")
		_ = fs.Parse(os.Args[2:])
		if err := installService(*conf, *name, *status); err != nil {
			fatal(err)
		}
		fmt.Println("служба запущена")
	case "remove-service":
		fs := flag.NewFlagSet("remove-service", flag.ExitOnError)
		name := fs.String("name", "", "имя шлюза")
		_ = fs.Parse(os.Args[2:])
		if err := removeService(*name); err != nil {
			fatal(err)
		}
		fmt.Println("служба убрана")
	case "service-state":
		fs := flag.NewFlagSet("service-state", flag.ExitOnError)
		name := fs.String("name", "", "имя шлюза")
		_ = fs.Parse(os.Args[2:])
		s, err := serviceState(*name)
		if err != nil {
			fatal(err)
		}
		fmt.Println(s)
	default:
		usage()
	}
}

func fatal(err error) {
	fmt.Fprintln(os.Stderr, "ошибка:", err)
	os.Exit(1)
}

// startNode поднимает сетевую карту и узел по файлу настроек. Возвращает функцию остановки.
func startNode(path, statusPath string, verbose bool) (stop func(), err error) {
	defer func() {
		if err != nil && statusPath != "" { // служба не может сказать ошибку словами — оставляем в файле состояния
			b, _ := json.Marshal(map[string]any{"version": version, "error": err.Error(), "time": time.Now().Unix()})
			_ = os.WriteFile(statusPath, b, 0o644)
		}
	}()
	fc, err := parseConfig(path)
	if err != nil {
		return nil, err
	}
	dev, err := tun.CreateTUN(fc.Name, fc.MTU)
	if err != nil {
		return nil, fmt.Errorf("не удалось создать сетевую карту: %w", err)
	}
	real, _ := dev.Name()
	undo, err := setupNetwork(dev, real, fc)
	if err != nil {
		dev.Close()
		return nil, err
	}
	e, err := NewEngine(fc.Engine, dev)
	if err != nil {
		undo()
		dev.Close()
		return nil, err
	}
	if verbose {
		e.Log = func(f string, a ...any) { fmt.Printf(time.Now().Format("15:04:05 ")+f+"\n", a...) }
	}
	if err := e.Start(); err != nil {
		undo()
		dev.Close()
		return nil, err
	}
	fmt.Printf("gsp %s: интерфейс %s, порт %d\n", version, real, e.LocalPort())
	hook := func() { e.NetworkChanged() }
	netChangeHook.Store(&hook)

	var prof func()
	if pf := os.Getenv("GSP_CPUPROFILE"); pf != "" { // для разработчика: где тратится время
		if f, err := os.Create(pf); err == nil {
			_ = pprof.StartCPUProfile(f)
			prof = pprof.StopCPUProfile
		}
	}
	done := make(chan struct{})
	if statusPath != "" {
		go func() {
			defer close(done)
			for !e.closed.Load() {
				writeStatus(statusPath, e)
				time.Sleep(time.Second)
			}
		}()
	} else {
		close(done)
	}
	return func() {
		netChangeHook.Store(nil)
		e.Close() // сначала прощаемся с сервером (маршрут к нему ещё на месте), потом убираем сеть
		<-done
		undo()
		if prof != nil {
			prof()
		}
		if statusPath != "" {
			_ = os.Remove(statusPath)
		}
	}, nil
}

func writeStatus(path string, e *Engine) {
	b, _ := json.Marshal(e.Status())
	_ = os.WriteFile(path+".tmp", b, 0o644)
	_ = os.Rename(path+".tmp", path)
}

func runEchoServer(key, peerKey, allowed string, port int) error {
	priv, err := keyFromB64(key)
	if err != nil {
		return fmt.Errorf("-key: %w", err)
	}
	peer, err := keyFromB64(peerKey)
	if err != nil {
		return fmt.Errorf("-peer: %w", err)
	}
	var al []netip.Prefix
	for _, a := range strings.Split(allowed, ",") {
		p, err := netip.ParsePrefix(strings.TrimSpace(a))
		if err != nil {
			return err
		}
		al = append(al, p)
	}
	pub, _ := pubFromPriv(priv)
	e, err := NewEngine(Config{PrivateKey: priv, ListenPort: port, FEC: "auto", Peers: []PeerConfig{{PublicKey: peer, Allowed: al}}}, newEchoDevice())
	if err != nil {
		return err
	}
	if err := e.Start(); err != nil {
		return err
	}
	fmt.Printf("echo-server port=%d pub=%s\n", e.LocalPort(), keyToB64(pub))
	sig := make(chan os.Signal, 1)
	signal.Notify(sig, os.Interrupt, syscall.SIGTERM)
	<-sig
	e.Close()
	return nil
}
