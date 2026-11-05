package main

import (
	"bufio"
	"encoding/json"
	"flag"
	"fmt"
	"os"
	"os/signal"
	"runtime/pprof"
	"syscall"
	"time"

	"golang.zx2c4.com/wireguard/tun"
)

const version = "0.1.0"

func usage() {
	fmt.Fprintln(os.Stderr, `gsp — Ghost Shell Protocol `+version+`
  gsp genkey                      новый закрытый ключ
  gsp pubkey                      открытый ключ по закрытому (читает со входа)
  gsp run -c файл [-status файл]  запустить (сервер или клиент — по настройкам)
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
		priv, err := keyFromB64(trim(line))
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
		if err := run(*conf, *status, *verbose); err != nil {
			fatal(err)
		}
	default:
		usage()
	}
}

func trim(s string) string {
	for len(s) > 0 && (s[len(s)-1] == '\n' || s[len(s)-1] == '\r' || s[len(s)-1] == ' ') {
		s = s[:len(s)-1]
	}
	return s
}

func fatal(err error) {
	fmt.Fprintln(os.Stderr, "ошибка:", err)
	os.Exit(1)
}

func run(path, statusPath string, verbose bool) error {
	fc, err := parseConfig(path)
	if err != nil {
		return err
	}
	dev, err := tun.CreateTUN(fc.Name, fc.MTU)
	if err != nil {
		return fmt.Errorf("не удалось создать сетевую карту: %w", err)
	}
	real, _ := dev.Name()
	if err := configureInterface(real, fc); err != nil {
		dev.Close()
		return err
	}
	e, err := NewEngine(fc.Engine, dev)
	if err != nil {
		dev.Close()
		return err
	}
	if verbose {
		e.Log = func(f string, a ...any) { fmt.Printf(time.Now().Format("15:04:05 ")+f+"\n", a...) }
	}
	if err := e.Start(); err != nil {
		dev.Close()
		return err
	}
	fmt.Printf("gsp %s: интерфейс %s, порт %d\n", version, real, e.LocalPort())
	if statusPath != "" {
		go func() {
			for !e.closed.Load() {
				b, _ := json.Marshal(e.Status())
				_ = os.WriteFile(statusPath+".tmp", b, 0o644)
				_ = os.Rename(statusPath+".tmp", statusPath)
				time.Sleep(time.Second)
			}
		}()
	}
	if pf := os.Getenv("GSP_CPUPROFILE"); pf != "" { // для разработчика: где тратится время
		if f, err := os.Create(pf); err == nil {
			_ = pprof.StartCPUProfile(f)
			defer pprof.StopCPUProfile()
		}
	}
	sig := make(chan os.Signal, 1)
	signal.Notify(sig, os.Interrupt, syscall.SIGTERM)
	<-sig
	e.Close()
	if statusPath != "" {
		_ = os.Remove(statusPath)
	}
	return nil
}
