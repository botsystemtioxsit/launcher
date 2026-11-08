package main

import (
	"net/netip"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func fcWith(ks string, allowed ...string) *FileConfig {
	pc := PeerConfig{}
	for _, a := range allowed {
		pc.Allowed = append(pc.Allowed, netip.MustParsePrefix(a))
	}
	return &FileConfig{KillSwitch: ks, Engine: Config{Peers: []PeerConfig{pc}}}
}

func TestWantKillSwitch(t *testing.T) {
	cases := []struct {
		name string
		fc   *FileConfig
		want bool
	}{
		{"auto: весь интернет в туннеле", fcWith("auto", "0.0.0.0/0"), true},
		{"auto: только IPv6 по умолчанию", fcWith("auto", "::/0"), true},
		{"auto: обе семьи", fcWith("auto", "0.0.0.0/0", "::/0"), true},
		{"auto: список «всё, кроме…» (две половины)", fcWith("auto", "0.0.0.0/1", "128.0.0.0/1"), false},
		{"auto: две сети", fcWith("auto", "10.77.0.1/32", "198.51.100.0/24"), false},
		{"auto: «ненастоящий» /0 с битами хоста не считается", fcWith("auto", "10.0.0.0/0"), false},
		{"on всегда", fcWith("on", "10.77.0.1/32"), true},
		{"off никогда", fcWith("off", "0.0.0.0/0"), false},
		{"пусто = как auto не включает", fcWith("", "10.0.0.0/8"), false},
	}
	for _, c := range cases {
		if got := wantKillSwitch(c.fc); got != c.want {
			t.Errorf("%s: получили %v, ждали %v", c.name, got, c.want)
		}
	}
}

func TestConfigKillSwitchKey(t *testing.T) {
	sk, _ := newKeypair()
	pk, _ := newKeypair()
	write := func(extra string) string {
		p := filepath.Join(t.TempDir(), "c.conf")
		body := "[Interface]\nPrivateKey = " + keyToB64(sk.Private) + "\nAddress = 10.77.0.2/32\n" + extra + "[Peer]\nPublicKey = " + keyToB64(pk.Public) + "\nEndpoint = 203.0.113.5:51821\nAllowedIPs = 0.0.0.0/0\n"
		if err := os.WriteFile(p, []byte(body), 0o600); err != nil {
			t.Fatal(err)
		}
		return p
	}
	fc, err := parseConfig(write(""))
	if err != nil || fc.KillSwitch != "auto" {
		t.Fatalf("по умолчанию должно быть auto: %v %v", fc, err)
	}
	for _, v := range []string{"on", "OFF", "Auto"} {
		fc, err := parseConfig(write("KillSwitch = " + v + "\n"))
		if err != nil || fc.KillSwitch != strings.ToLower(v) {
			t.Fatalf("KillSwitch = %s: %v %v", v, fc, err)
		}
	}
	if _, err := parseConfig(write("KillSwitch = maybe\n")); err == nil {
		t.Fatal("непонятное значение должно отклоняться")
	}
}
