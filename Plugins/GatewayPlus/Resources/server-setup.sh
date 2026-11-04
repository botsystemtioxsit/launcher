#!/usr/bin/env bash
# Ghost Shell Gateway: ставит WireGuard на ЭТОТ сервер и печатает готовый конфиг для компьютера.
# Подходит для Ubuntu 22.04/24.04 и Debian 12. Запускать от root (sudo -i).
set -euo pipefail

if [ "$(id -u)" -ne 0 ]; then
  echo "Запустите от root: введите  sudo -i  и вставьте скрипт ещё раз." >&2
  exit 1
fi

# на только что созданном сервере apt нередко занят автообновлением — ждём до 5 минут, а не падаем
export DEBIAN_FRONTEND=noninteractive
APT="apt-get -o DPkg::Lock::Timeout=300 -y"
$APT update
$APT install wireguard iptables curl

umask 077
mkdir -p /etc/wireguard
cd /etc/wireguard

PORT=51820
SUBNET=10.66.66
IFACE="$(ip -4 route show default | awk '{print $5; exit}')"
PUBIP="$(curl -4 -fsS --max-time 10 https://api.ipify.org || curl -4 -fsS --max-time 10 https://ifconfig.me || true)"
if [ -z "$IFACE" ] || [ -z "$PUBIP" ]; then
  echo "Не удалось узнать сетевой интерфейс или внешний адрес сервера." >&2
  exit 1
fi

wg genkey | tee server.key | wg pubkey > server.pub
wg genkey | tee client.key | wg pubkey > client.pub
wg genpsk > client.psk

cat > wg0.conf <<CONF
[Interface]
Address = ${SUBNET}.1/24
ListenPort = ${PORT}
PrivateKey = $(cat server.key)
PostUp = iptables -A FORWARD -i wg0 -j ACCEPT; iptables -A FORWARD -o wg0 -j ACCEPT; iptables -t nat -A POSTROUTING -o ${IFACE} -j MASQUERADE; iptables -t mangle -A FORWARD -p tcp --tcp-flags SYN,RST SYN -j TCPMSS --clamp-mss-to-pmtu
PostDown = iptables -D FORWARD -i wg0 -j ACCEPT; iptables -D FORWARD -o wg0 -j ACCEPT; iptables -t nat -D POSTROUTING -o ${IFACE} -j MASQUERADE; iptables -t mangle -D FORWARD -p tcp --tcp-flags SYN,RST SYN -j TCPMSS --clamp-mss-to-pmtu

[Peer]
PublicKey = $(cat client.pub)
PresharedKey = $(cat client.psk)
AllowedIPs = ${SUBNET}.2/32
CONF

echo 'net.ipv4.ip_forward=1' > /etc/sysctl.d/99-wireguard.conf
# быстрее на длинных расстояниях и при потерях: очередь fq + алгоритм BBR (если ядро умеет; нет — тихо пропускаем)
if modprobe tcp_bbr 2>/dev/null && grep -qw bbr /proc/sys/net/ipv4/tcp_available_congestion_control 2>/dev/null; then
  printf 'net.core.default_qdisc=fq\nnet.ipv4.tcp_congestion_control=bbr\n' > /etc/sysctl.d/99-ghostshell-net.conf
fi
sysctl --system > /dev/null

# если на сервере включён файрвол ufw — открываем порт шлюза
if command -v ufw > /dev/null 2>&1 && ufw status | grep -q "Status: active"; then
  ufw allow "${PORT}/udp"
fi

systemctl enable wg-quick@wg0
systemctl restart wg-quick@wg0

echo
echo "=========== НАЧАЛО КОНФИГА (скопируйте всё между линиями) ==========="
cat <<CLIENT
[Interface]
PrivateKey = $(cat client.key)
Address = ${SUBNET}.2/32
MTU = 1380
DNS = 1.1.1.1, 9.9.9.9

[Peer]
PublicKey = $(cat server.pub)
PresharedKey = $(cat client.psk)
Endpoint = ${PUBIP}:${PORT}
AllowedIPs = 0.0.0.0/0, ::/0
PersistentKeepalive = 25
CLIENT
echo "=========== КОНЕЦ КОНФИГА ==========="
echo
echo "Готово. Если шлюз не подключается: в панели хостинга откройте порт ${PORT} по протоколу UDP."
