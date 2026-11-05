#!/usr/bin/env bash
# Сравнение GSP и WireGuard на одной машине: три сетевые «комнаты» (клиент — прокладка — сервер).
#   bench/run.sh <каталог с бинарниками gsp, relay, wireguard-go> [потери,%] [задержка в одну сторону, мс] [секунд]
# Нужны root, ip, iperf3, wg. Всё, что создано, убирается в конце.
set -u
BIN=${1:?каталог с бинарниками}; LOSS=${2:-0}; DELAY=${3:-0}; SECS=${4:-8}; CC=${5:-cubic}
GSP=$BIN/gsp; RELAY=$BIN/relay; WGGO=$BIN/wireguard-go
W=$(mktemp -d); MTU=1380
cleanup() {
  pkill -f "$W/" 2>/dev/null; pkill -f "$GSP run" 2>/dev/null; pkill -f "$WGGO" 2>/dev/null; pkill -f "$RELAY" 2>/dev/null; pkill iperf3 2>/dev/null
  for n in bcli bsrv bmid; do ip netns del $n 2>/dev/null; done
  rm -rf "$W"
}
trap cleanup EXIT
cleanup_ns() { for n in bcli bsrv bmid; do ip netns del $n 2>/dev/null; done; }
cleanup_ns
for n in bcli bsrv bmid; do ip netns add $n; ip netns exec $n ip link set lo up; done
ip link add c0 type veth peer name m0; ip link set c0 netns bcli; ip link set m0 netns bmid
ip link add s0 type veth peer name m1; ip link set s0 netns bsrv; ip link set m1 netns bmid
ip netns exec bcli ip addr add 10.0.1.2/24 dev c0; ip netns exec bcli ip link set c0 up; ip netns exec bcli ip route add default via 10.0.1.1
ip netns exec bsrv ip addr add 10.0.2.2/24 dev s0; ip netns exec bsrv ip link set s0 up; ip netns exec bsrv ip route add default via 10.0.2.1
ip netns exec bmid ip addr add 10.0.1.1/24 dev m0; ip netns exec bmid ip addr add 10.0.2.1/24 dev m1
ip netns exec bmid ip link set m0 up; ip netns exec bmid ip link set m1 up; ip netns exec bmid sysctl -qw net.ipv4.ip_forward=1

if [ "$LOSS" != 0 ] || [ "$DELAY" != 0 ]; then
  ip netns exec bmid "$RELAY" -l 10.0.1.1:7000 -s 10.0.2.2:7001 -b 10.0.2.1 -loss "$LOSS" -delay "${DELAY}ms" >"$W/relay.log" 2>&1 &
  EP=10.0.1.1:7000
else
  EP=10.0.2.2:7001
fi
sleep 0.5

run_iperf() { # $1 = имя режима
  local down up
  start_srv() { pkill iperf3 2>/dev/null; sleep 1; ip netns exec bsrv iperf3 -s -B 10.77.0.1 -D -1 --logfile "$W/iperf-s.log" >/dev/null 2>&1; sleep 0.7; }
  mbit() { python3 -c "import sys,json; d=json.load(sys.stdin); print('%.1f' % (d['end']['sum_received']['bits_per_second']/1e6))" 2>/dev/null || echo "ошибка"; }
  # «вниз» — клиент качает с сервера (самый частый случай), «вверх» — отдаёт
  start_srv; down=$(ip netns exec bcli iperf3 -c 10.77.0.1 -t "$SECS" -R -C "$CC" -J 2>/dev/null | mbit)
  start_srv; up=$(ip netns exec bcli iperf3 -c 10.77.0.1 -t "$SECS" -C "$CC" -J 2>/dev/null | mbit)
  printf '%-24s вниз %9s Мбит/с   вверх %9s Мбит/с\n' "$1" "$down" "$up"
  pkill iperf3 2>/dev/null; sleep 0.3
}
ping_ok() { ip netns exec bcli timeout 5 bash -c 'for i in 1 2 3 4 5 6 7 8 9 10; do ping -c1 -W1 10.77.0.1 >/dev/null 2>&1 && exit 0; sleep 0.5; done; exit 1'; }

# ---------- GSP ----------
gsp_test() { # $1 = FEC, $2 = подпись
  local sk ck sp cp
  sk=$($GSP genkey); ck=$($GSP genkey); sp=$(echo "$sk" | $GSP pubkey); cp=$(echo "$ck" | $GSP pubkey)
  cat >"$W/s.conf" <<C
[Interface]
PrivateKey = $sk
Address = 10.77.0.1/24
ListenPort = 7001
MTU = $MTU
FEC = $1
Name = gsps
[Peer]
PublicKey = $cp
AllowedIPs = 10.77.0.2/32
C
  cat >"$W/c.conf" <<C
[Interface]
PrivateKey = $ck
Address = 10.77.0.2/32
MTU = $MTU
FEC = $1
Name = gspc
[Peer]
PublicKey = $sp
Endpoint = $EP
AllowedIPs = 10.77.0.1/32
PersistentKeepalive = 5
C
  ip netns exec bsrv "$GSP" run -c "$W/s.conf" >"$W/gsps.log" 2>&1 &
  ip netns exec bcli "$GSP" run -c "$W/c.conf" >"$W/gspc.log" 2>&1 &
  sleep 1
  ip netns exec bcli ip route add 10.77.0.1/32 dev gspc 2>/dev/null
  if ping_ok; then run_iperf "$2"; else echo "$2: туннель не поднялся"; cat "$W/gspc.log" "$W/gsps.log" | tail -5; fi
  pkill -f "$GSP run"; sleep 0.5
}

# ---------- wireguard-go ----------
wg_test() {
  local sk ck sp cp
  sk=$(wg genkey); ck=$(wg genkey); sp=$(echo "$sk" | wg pubkey); cp=$(echo "$ck" | wg pubkey)
  ip netns exec bsrv "$WGGO" -f wgs >"$W/wgs.log" 2>&1 &
  ip netns exec bcli "$WGGO" -f wgc >"$W/wgc.log" 2>&1 &
  sleep 1
  cat >"$W/ws.conf" <<C
[Interface]
PrivateKey = $sk
ListenPort = 7001
[Peer]
PublicKey = $cp
AllowedIPs = 10.77.0.2/32
C
  cat >"$W/wc.conf" <<C
[Interface]
PrivateKey = $ck
[Peer]
PublicKey = $sp
Endpoint = $EP
AllowedIPs = 10.77.0.1/32
PersistentKeepalive = 5
C
  ip netns exec bsrv wg setconf wgs "$W/ws.conf"; ip netns exec bcli wg setconf wgc "$W/wc.conf"
  ip netns exec bsrv ip addr add 10.77.0.1/24 dev wgs; ip netns exec bsrv ip link set wgs mtu $MTU up
  ip netns exec bcli ip addr add 10.77.0.2/32 dev wgc; ip netns exec bcli ip link set wgc mtu $MTU up
  ip netns exec bcli ip route add 10.77.0.1/32 dev wgc
  if ping_ok; then run_iperf "wireguard-go"; else echo "wireguard-go: туннель не поднялся"; tail -5 "$W/wgc.log"; fi
  pkill -f "$WGGO"; sleep 0.5
}

# ---------- WireGuard в ядре (если есть) ----------
wgk_test() {
  ip link add wgprobe type wireguard 2>/dev/null || { echo "WireGuard в ядре: недоступен на этой машине"; return; }
  ip link del wgprobe
  local sk ck sp cp
  sk=$(wg genkey); ck=$(wg genkey); sp=$(echo "$sk" | wg pubkey); cp=$(echo "$ck" | wg pubkey)
  ip netns exec bsrv ip link add wgs type wireguard; ip netns exec bcli ip link add wgc type wireguard
  printf '[Interface]\nPrivateKey = %s\nListenPort = 7001\n[Peer]\nPublicKey = %s\nAllowedIPs = 10.77.0.2/32\n' "$sk" "$cp" >"$W/ks.conf"
  printf '[Interface]\nPrivateKey = %s\n[Peer]\nPublicKey = %s\nEndpoint = %s\nAllowedIPs = 10.77.0.1/32\nPersistentKeepalive = 5\n' "$ck" "$sp" "$EP" >"$W/kc.conf"
  ip netns exec bsrv wg setconf wgs "$W/ks.conf"; ip netns exec bcli wg setconf wgc "$W/kc.conf"
  ip netns exec bsrv ip addr add 10.77.0.1/24 dev wgs; ip netns exec bsrv ip link set wgs mtu $MTU up
  ip netns exec bcli ip addr add 10.77.0.2/32 dev wgc; ip netns exec bcli ip link set wgc mtu $MTU up
  ip netns exec bcli ip route add 10.77.0.1/32 dev wgc
  if ping_ok; then run_iperf "WireGuard (ядро)"; else echo "WireGuard (ядро): туннель не поднялся"; fi
  ip netns exec bsrv ip link del wgs; ip netns exec bcli ip link del wgc
}

echo "=== потери ${LOSS}%, задержка ${DELAY} мс в каждую сторону, замер ${SECS} с ==="
wgk_test
wg_test
gsp_test off "GSP (без защиты)"
gsp_test auto "GSP (авто-защита)"
gsp_test 10+2 "GSP (защита 10+2)"
gsp_test 8+3 "GSP (защита 8+3)"
