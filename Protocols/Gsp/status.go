package main

import "time"

type PeerStatus struct {
	Key          string  `json:"key"`
	Endpoint     string  `json:"endpoint"`
	Connected    bool    `json:"connected"`
	SessionAgeS  int     `json:"sessionAgeS"`
	LastRecvAgoS int     `json:"lastRecvAgoS"`
	TxBytes      uint64  `json:"txBytes"`
	RxBytes      uint64  `json:"rxBytes"`
	TxPackets    uint64  `json:"txPackets"`
	RxPackets    uint64  `json:"rxPackets"`
	Recovered    uint64  `json:"recovered"`
	LossPct      float64 `json:"lossPct"`
	FEC          string  `json:"fec"`
	Proto        int     `json:"proto"` // версия протокола текущего сеанса (1 или 2)
}

type Status struct {
	Version string       `json:"version"`
	Port    int          `json:"port"`
	HsDrop  uint64       `json:"hsDropped"`  // рукопожатий отброшено ограничителем (сервер под нагрузкой)
	KillSw  bool         `json:"killSwitch"` // защита от утечек (Windows) включена
	Peers   []PeerStatus `json:"peers"`
}

func (e *Engine) Status() Status {
	st := Status{Version: version, Port: e.LocalPort(), HsDrop: e.hsDrop.Load(), KillSw: killSwitchOn.Load()}
	now := time.Now()
	for _, p := range e.snapshotPeers() {
		ps := PeerStatus{Key: keyToB64(p.pub[:]), TxBytes: p.txBytes.Load(), RxBytes: p.rxBytes.Load(),
			TxPackets: p.txPk.Load(), RxPackets: p.rxPk.Load(), Recovered: p.recovered.Load(), FEC: "off"}
		if ep := p.endpoint.Load(); ep != nil {
			ps.Endpoint = ep.String()
		}
		if s := p.cur.Load(); s != nil {
			ps.Connected = true
			ps.Proto = s.ver
			ps.SessionAgeS = int(now.Sub(s.created).Seconds())
		}
		if lr := p.lastRecv.Load(); lr > 0 {
			ps.LastRecvAgoS = int(now.Sub(time.Unix(0, lr)).Seconds())
		}
		p.fmu.Lock()
		ps.LossPct = p.lossPct
		if p.enc.k > 0 {
			ps.FEC = itoa(p.enc.k) + "+" + itoa(p.enc.m)
		}
		p.fmu.Unlock()
		st.Peers = append(st.Peers, ps)
	}
	return st
}

func itoa(n int) string {
	if n == 0 {
		return "0"
	}
	var b [8]byte
	i := len(b)
	for n > 0 {
		i--
		b[i] = byte('0' + n%10)
		n /= 10
	}
	return string(b[i:])
}
