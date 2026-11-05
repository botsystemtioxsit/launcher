package main

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func writeConf(t *testing.T, body string) string {
	p := filepath.Join(t.TempDir(), "x.conf")
	if err := os.WriteFile(p, []byte(body), 0o600); err != nil {
		t.Fatal(err)
	}
	return p
}

func TestConfigParseOK(t *testing.T) {
	k, _ := newKeypair()
	body := "[Interface]\nPrivateKey = " + keyToB64(k.Private) + "\nAddress = 10.77.0.2/32, fd77::2/128\nMTU = 1380\nFEC = 8+3\nPad = 64\n" +
		"[Peer]\nPublicKey = " + keyToB64(k.Public) + "\nEndpoint = 203.0.113.5:51821\nAllowedIPs = 0.0.0.0/0, ::/0\nPersistentKeepalive = 25 # комментарий\n"
	fc, err := parseConfig(writeConf(t, body))
	if err != nil {
		t.Fatal(err)
	}
	if fc.MTU != 1380 || fc.Engine.FEC != "8+3" || fc.Engine.PadMax != 64 || len(fc.Address) != 2 {
		t.Fatalf("разобрано неверно: %+v", fc)
	}
	if len(fc.Engine.Peers) != 1 || len(fc.Engine.Peers[0].Allowed) != 2 || fc.Engine.Peers[0].Keepalive.Seconds() != 25 {
		t.Fatalf("собеседник разобран неверно: %+v", fc.Engine.Peers)
	}
}

func TestConfigRejectsBad(t *testing.T) {
	k, _ := newKeypair()
	good := keyToB64(k.Private)
	cases := map[string]string{
		"нет ключа":         "[Interface]\nMTU = 1380\n[Peer]\nPublicKey = " + keyToB64(k.Public) + "\n",
		"нет собеседника":   "[Interface]\nPrivateKey = " + good + "\n",
		"плохой MTU":        "[Interface]\nPrivateKey = " + good + "\nMTU = 90\n[Peer]\nPublicKey = " + keyToB64(k.Public) + "\n",
		"неизвестная опция": "[Interface]\nPrivateKey = " + good + "\nPostUp = rm -rf /\n[Peer]\nPublicKey = " + keyToB64(k.Public) + "\n",
		"плохой ключ":       "[Interface]\nPrivateKey = abc\n[Peer]\nPublicKey = " + keyToB64(k.Public) + "\n",
	}
	for name, body := range cases {
		if _, err := parseConfig(writeConf(t, body)); err == nil {
			t.Errorf("%s: файл должен быть отклонён", name)
		}
	}
	// IPv6-адрес сервера пока не поддерживается и должен отказываться понятно
	body := "[Interface]\nPrivateKey = " + good + "\n[Peer]\nPublicKey = " + keyToB64(k.Public) + "\nEndpoint = [2001:db8::1]:51821\nAllowedIPs = 0.0.0.0/0\n"
	fc, err := parseConfig(writeConf(t, body))
	if err != nil {
		t.Fatal(err)
	}
	if _, err := NewEngine(fc.Engine, newFakeDev()); err == nil || !strings.Contains(err.Error(), "IPv4") {
		t.Fatalf("ожидался отказ из-за IPv6-адреса сервера, получено %v", err)
	}
}
