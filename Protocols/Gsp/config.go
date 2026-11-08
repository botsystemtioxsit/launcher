package main

import (
	"bufio"
	"errors"
	"fmt"
	"net/netip"
	"os"
	"strconv"
	"strings"
	"time"
)

// Файл настроек — как у WireGuard, чтобы привычно было читать:
//
//	[Interface]
//	PrivateKey = ...
//	Address    = 10.77.0.2/32, fd77::2/128
//	DNS        = 1.1.1.1, 9.9.9.9     (клиент на Windows; необязательно)
//	ListenPort = 51821        (у сервера)
//	MTU        = 1380
//	FEC        = auto | off | 10+2
//	Pad        = 0            (случайная набивка пакетов до N байт, для маскировки размеров)
//	Name       = gsp0
//	Proto      = 1 | 2      (версия протокола у клиента; без строки — 1, чтобы прежние профили работали; новые профили — 2; сервер понимает обе)
//	KillSwitch = auto | on | off   (Windows: «защита от утечек» — пока шлюз работает, весь трафик вне туннеля заблокирован;
//	                                auto: включена, когда в туннель идёт весь интернет, то есть в AllowedIPs есть 0.0.0.0/0)
//	[Peer]
//	PublicKey  = ...
//	Endpoint   = 203.0.113.5:51821   (у клиента)
//	AllowedIPs = 0.0.0.0/0
//	PersistentKeepalive = 25
type FileConfig struct {
	Engine  Config
	Name    string
	MTU     int
	Address []netip.Prefix
	DNS     []netip.Addr // сервера имён внутри туннеля (Windows ставит их на сетевую карту шлюза)
	// Защита от утечек (Windows): "auto" (по умолчанию), "on" или "off". См. killswitch.go.
	KillSwitch string
}

func parseConfig(path string) (*FileConfig, error) {
	f, err := os.Open(path)
	if err != nil {
		return nil, err
	}
	defer f.Close()
	fc := &FileConfig{Name: "gsp0", MTU: 1380, KillSwitch: "auto"}
	var cur *PeerConfig
	section := ""
	sc := bufio.NewScanner(f)
	line := 0
	flush := func() error {
		if cur != nil {
			if len(cur.PublicKey) != 32 {
				return errors.New("у [Peer] нет PublicKey")
			}
			fc.Engine.Peers = append(fc.Engine.Peers, *cur)
			cur = nil
		}
		return nil
	}
	for sc.Scan() {
		line++
		t := strings.TrimSpace(sc.Text())
		if i := strings.IndexByte(t, '#'); i >= 0 {
			t = strings.TrimSpace(t[:i])
		}
		if t == "" {
			continue
		}
		if strings.HasPrefix(t, "[") {
			if err := flush(); err != nil {
				return nil, err
			}
			section = strings.ToLower(strings.Trim(t, "[] "))
			if section == "peer" {
				cur = &PeerConfig{}
			} else if section != "interface" {
				return nil, fmt.Errorf("строка %d: неизвестный раздел %s", line, t)
			}
			continue
		}
		k, v, ok := strings.Cut(t, "=")
		if !ok {
			return nil, fmt.Errorf("строка %d: ожидалось «ключ = значение»", line)
		}
		k, v = strings.ToLower(strings.TrimSpace(k)), strings.TrimSpace(v)
		var err error
		switch section {
		case "interface":
			switch k {
			case "privatekey":
				fc.Engine.PrivateKey, err = keyFromB64(v)
			case "address":
				for _, a := range strings.Split(v, ",") {
					pf, e := netip.ParsePrefix(strings.TrimSpace(a))
					if e != nil {
						err = e
						break
					}
					fc.Address = append(fc.Address, pf)
				}
			case "dns":
				for _, a := range strings.Split(v, ",") {
					ad, e := netip.ParseAddr(strings.TrimSpace(a))
					if e != nil {
						err = e
						break
					}
					fc.DNS = append(fc.DNS, ad)
				}
			case "listenport":
				fc.Engine.ListenPort, err = strconv.Atoi(v)
			case "mtu":
				fc.MTU, err = strconv.Atoi(v)
				if err == nil && (fc.MTU < 1200 || fc.MTU > 1500) {
					err = errors.New("MTU должен быть от 1200 до 1500")
				}
			case "fec":
				fc.Engine.FEC = strings.ToLower(v)
			case "pad":
				fc.Engine.PadMax, err = strconv.Atoi(v)
				if err == nil && (fc.Engine.PadMax < 0 || fc.Engine.PadMax > 255) {
					err = errors.New("Pad: от 0 до 255")
				}
			case "name":
				fc.Name = v
			case "proto":
				fc.Engine.Proto, err = strconv.Atoi(v)
				if err == nil && fc.Engine.Proto != 1 && fc.Engine.Proto != 2 {
					err = errors.New("Proto: 1 или 2")
				}
			case "killswitch":
				switch strings.ToLower(v) {
				case "auto", "on", "off":
					fc.KillSwitch = strings.ToLower(v)
				default:
					err = errors.New("KillSwitch: auto, on или off")
				}
			case "debugdroptx":
				fc.Engine.DebugDropTx, err = strconv.ParseFloat(v, 64)
			default:
				err = fmt.Errorf("неизвестная настройка %q", k)
			}
		case "peer":
			switch k {
			case "publickey":
				cur.PublicKey, err = keyFromB64(v)
			case "endpoint":
				cur.Endpoint, err = netip.ParseAddrPort(v)
			case "allowedips":
				for _, a := range strings.Split(v, ",") {
					pf, e := netip.ParsePrefix(strings.TrimSpace(a))
					if e != nil {
						err = e
						break
					}
					cur.Allowed = append(cur.Allowed, pf)
				}
			case "persistentkeepalive":
				var n int
				n, err = strconv.Atoi(v)
				cur.Keepalive = time.Duration(n) * time.Second
			default:
				err = fmt.Errorf("неизвестная настройка %q", k)
			}
		default:
			err = errors.New("настройка вне раздела")
		}
		if err != nil {
			return nil, fmt.Errorf("строка %d: %v", line, err)
		}
	}
	if err := flush(); err != nil {
		return nil, err
	}
	if len(fc.Engine.PrivateKey) != 32 {
		return nil, errors.New("нет PrivateKey")
	}
	if len(fc.Engine.Peers) == 0 {
		return nil, errors.New("нет ни одного [Peer]")
	}
	return fc, nil
}
