#!/usr/bin/env bash
# Ghost Shell Gateway: ставит сервер GSP (свой протокол, лучше переносит потери пакетов) на ЭТОТ сервер и печатает готовый конфиг для компьютера.
# Подходит для Ubuntu 22.04/24.04 и Debian 12 (x86_64 и arm64). Запускать от root (sudo -i). WireGuard, если он уже стоит, не трогается:
# GSP живёт на другом порту (51821/udp) и в другой сети (10.78.0.0/24).
set -euo pipefail

if [ "$(id -u)" -ne 0 ]; then
  echo "Запустите от root: введите  sudo -i  и вставьте скрипт ещё раз." >&2
  exit 1
fi

PORT=51821
SUBNET=10.78.0
BASE="${GSP_BASE_URL:-https://github.com/botsystemtioxsit/launcher/releases/latest/download}"

export DEBIAN_FRONTEND=noninteractive
APT="apt-get -o DPkg::Lock::Timeout=300 -y"
if [ "${GSP_NO_APT:-}" != "1" ]; then
  # на только что созданном сервере apt нередко занят автообновлением — ждём до 5 минут, а не падаем
  $APT update
  $APT install iptables curl ca-certificates
fi

case "$(uname -m)" in
  x86_64|amd64) ARCH=amd64 ;;
  aarch64|arm64) ARCH=arm64 ;;
  *) echo "Этот процессор ($(uname -m)) не поддерживается: нужен x86_64 или arm64." >&2; exit 1 ;;
esac

# сетевая карта для виртуальной сети (TUN): у контейнерных VPS (OpenVZ, часть LXC) её нет, пока не включить в панели хостера
if [ ! -c /dev/net/tun ]; then
  mkdir -p /dev/net && mknod /dev/net/tun c 10 200 2>/dev/null && chmod 600 /dev/net/tun || true
fi
if [ ! -c /dev/net/tun ]; then
  echo "На сервере нет устройства TUN (/dev/net/tun). Обычно это контейнерный VPS: включите «TUN/TAP» в панели хостера или возьмите обычный (KVM) сервер." >&2
  exit 1
fi

IFACE="$(ip -4 route show default | awk '{print $5; exit}')"
PUBIP="${GSP_PUBIP:-$(curl -4 -fsS --max-time 10 https://api.ipify.org || curl -4 -fsS --max-time 10 https://ifconfig.me || true)}"
if [ -z "$IFACE" ] || [ -z "$PUBIP" ]; then
  echo "Не удалось узнать сетевой интерфейс или внешний адрес сервера." >&2
  exit 1
fi

# --- программа gsp: скачиваем из релиза лаунчера и проверяем сумму (или берём готовый файл при проверке: GSP_BIN) ---
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
if [ -n "${GSP_BIN:-}" ]; then
  cp "$GSP_BIN" "$TMP/gsp"
else
  echo "Скачиваю gsp ($ARCH)…"
  curl -fsSL --retry 3 --max-time 180 -o "$TMP/gsp" "$BASE/gsp-linux-$ARCH"
  curl -fsSL --retry 3 --max-time 30 -o "$TMP/gsp.sha256" "$BASE/gsp-linux-$ARCH.sha256"
  WANT="$(awk '{print tolower($1); exit}' "$TMP/gsp.sha256")"
  GOT="$(sha256sum "$TMP/gsp" | awk '{print $1}')"
  if [ -z "$WANT" ] || [ "$WANT" != "$GOT" ]; then
    echo "Контрольная сумма программы gsp не совпала — не устанавливаю. Ожидалось: $WANT, получено: $GOT" >&2
    exit 1
  fi
  echo "Контрольная сумма совпала."
fi
chmod 755 "$TMP/gsp"
"$TMP/gsp" version >/dev/null || { echo "Скачанная программа gsp не запускается на этом сервере." >&2; exit 1; }

systemctl stop gsp 2>/dev/null || true
install -m 755 "$TMP/gsp" /usr/local/bin/gsp

# --- ключи и настройки ---
umask 077
mkdir -p /etc/gsp
SK="$(/usr/local/bin/gsp genkey)"; SP="$(printf '%s\n' "$SK" | /usr/local/bin/gsp pubkey)"
CK="$(/usr/local/bin/gsp genkey)"; CP="$(printf '%s\n' "$CK" | /usr/local/bin/gsp pubkey)"

cat > /etc/gsp/gsp0.conf <<CONF
[Interface]
PrivateKey = ${SK}
Address = ${SUBNET}.1/24
ListenPort = ${PORT}
MTU = 1380
FEC = auto
Name = gsp0

[Peer]
PublicKey = ${CP}
AllowedIPs = ${SUBNET}.2/32
CONF
chmod 600 /etc/gsp/gsp0.conf

# --- пересылка и NAT: в виде маленького помощника, который служба вызывает при запуске и остановке ---
printf 'net.ipv4.ip_forward=1\n' > /etc/sysctl.d/99-gsp.conf
sysctl --system > /dev/null

cat > /usr/local/sbin/gsp-fw <<'FW'
#!/bin/sh
# правила пересылки и NAT для шлюза GSP (вызывается службой gsp)
SUBNET="__SUBNET__.0/24"
IFACE="$(ip -4 route show default | awk '{print $5; exit}')"
del() {
  while iptables -D FORWARD -i gsp0 -j ACCEPT 2>/dev/null; do :; done
  while iptables -D FORWARD -o gsp0 -j ACCEPT 2>/dev/null; do :; done
  while iptables -t nat -D POSTROUTING -s "$SUBNET" -o "$IFACE" -j MASQUERADE 2>/dev/null; do :; done
  while iptables -t mangle -D FORWARD -i gsp0 -p tcp --tcp-flags SYN,RST SYN -j TCPMSS --clamp-mss-to-pmtu 2>/dev/null; do :; done
  while iptables -t mangle -D FORWARD -o gsp0 -p tcp --tcp-flags SYN,RST SYN -j TCPMSS --clamp-mss-to-pmtu 2>/dev/null; do :; done
}
case "$1" in
  up)
    del
    iptables -I FORWARD 1 -i gsp0 -j ACCEPT
    iptables -I FORWARD 1 -o gsp0 -j ACCEPT
    iptables -t nat -A POSTROUTING -s "$SUBNET" -o "$IFACE" -j MASQUERADE
    iptables -t mangle -A FORWARD -i gsp0 -p tcp --tcp-flags SYN,RST SYN -j TCPMSS --clamp-mss-to-pmtu
    iptables -t mangle -A FORWARD -o gsp0 -p tcp --tcp-flags SYN,RST SYN -j TCPMSS --clamp-mss-to-pmtu
    ;;
  down) del ;;
esac
exit 0
FW
sed -i "s#__SUBNET__#${SUBNET}#" /usr/local/sbin/gsp-fw
chmod 755 /usr/local/sbin/gsp-fw

cat > /etc/systemd/system/gsp.service <<'UNIT'
[Unit]
Description=Ghost Shell Protocol (GSP) server
After=network-online.target
Wants=network-online.target

[Service]
ExecStart=/usr/local/bin/gsp run -c /etc/gsp/gsp0.conf -status /run/gsp/status.json
ExecStartPost=/usr/local/sbin/gsp-fw up
ExecStopPost=/usr/local/sbin/gsp-fw down
RuntimeDirectory=gsp
Restart=always
RestartSec=2
LimitNOFILE=65535
NoNewPrivileges=true
ProtectHome=true
PrivateTmp=true

[Install]
WantedBy=multi-user.target
UNIT

# если на сервере включён файрвол ufw — открываем порт GSP
if command -v ufw > /dev/null 2>&1 && ufw status | grep -q "Status: active"; then
  ufw allow "${PORT}/udp"
fi

systemctl daemon-reload
systemctl enable gsp > /dev/null 2>&1
systemctl restart gsp

# ждём, пока сервер поднимется
OK=0
for _ in $(seq 1 20); do
  if systemctl is-active --quiet gsp && ip link show gsp0 > /dev/null 2>&1; then OK=1; break; fi
  sleep 0.5
done
if [ "$OK" != "1" ]; then
  echo "Служба gsp не запустилась. Журнал:" >&2
  journalctl -u gsp -n 15 --no-pager >&2 || true
  exit 1
fi
echo "Сервер GSP запущен: порт ${PORT}/udp, сеть ${SUBNET}.0/24."

echo
echo "=========== НАЧАЛО КОНФИГА (скопируйте всё между линиями) ==========="
cat <<CLIENT
# GSP
[Interface]
PrivateKey = ${CK}
Address = ${SUBNET}.2/32
MTU = 1380
DNS = 1.1.1.1, 9.9.9.9
FEC = auto

[Peer]
PublicKey = ${SP}
Endpoint = ${PUBIP}:${PORT}
AllowedIPs = 0.0.0.0/0
PersistentKeepalive = 25
CLIENT
echo "=========== КОНЕЦ КОНФИГА ==========="
echo
echo "Готово. Если шлюз не подключается: в панели хостинга откройте порт ${PORT} по протоколу UDP."
