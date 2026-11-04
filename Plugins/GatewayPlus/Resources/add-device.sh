#!/usr/bin/env bash
# Ghost Shell Gateway: добавляет на УЖЕ настроенный WireGuard-сервер ещё одно устройство (например, телефон) со своим ключом
# и печатает для него готовый конфиг. Старые устройства не затрагиваются. Запускать от root.
set -euo pipefail

if [ "$(id -u)" -ne 0 ]; then echo "Нужен root." >&2; exit 1; fi
NAME="${DEVNAME:-phone}"
cd /etc/wireguard
if [ ! -f wg0.conf ] || [ ! -f server.key ]; then
  echo "WireGuard на этом сервере не настроен мастером Gateway Plus (нет /etc/wireguard/wg0.conf)." >&2
  exit 2
fi
umask 077

PORT="$(awk -F' *= *' '/^ListenPort/{print $2; exit}' wg0.conf)"
SADDR="$(awk -F' *= *' '/^Address/{print $2; exit}' wg0.conf)"
SUBNET="${SADDR%.*}"
if [ -z "$PORT" ] || [ -z "$SUBNET" ]; then echo "Не получилось прочитать настройки сервера из wg0.conf." >&2; exit 2; fi

USED="$(grep -F "AllowedIPs = ${SUBNET}." wg0.conf | sed -E 's#.*\.([0-9]+)/32.*#\1#' || true)"
N=2
while printf '%s\n' "$USED" | grep -qx "$N"; do N=$((N+1)); done
if [ "$N" -gt 254 ]; then echo "На сервере нет свободных адресов для устройств." >&2; exit 3; fi

PUBIP="$(curl -4 -fsS --max-time 10 https://api.ipify.org || curl -4 -fsS --max-time 10 https://ifconfig.me || true)"
if [ -z "$PUBIP" ]; then echo "Не удалось узнать внешний адрес сервера." >&2; exit 1; fi

KEY="$(wg genkey)"
PUB="$(printf '%s' "$KEY" | wg pubkey)"
PSK="$(wg genpsk)"
SPUB="$(wg pubkey < server.key)"

cat >> wg0.conf <<CONF

[Peer]
# ghostshell: ${NAME}
PublicKey = ${PUB}
PresharedKey = ${PSK}
AllowedIPs = ${SUBNET}.${N}/32
CONF

# новый ключ подхватывается без обрыва уже подключённых устройств
if wg show wg0 > /dev/null 2>&1; then
  wg syncconf wg0 <(wg-quick strip wg0)
else
  systemctl restart wg-quick@wg0
fi

echo
echo "=========== НАЧАЛО КОНФИГА (скопируйте всё между линиями) ==========="
cat <<CLIENT
[Interface]
PrivateKey = ${KEY}
Address = ${SUBNET}.${N}/32
MTU = 1380
DNS = 1.1.1.1, 9.9.9.9

[Peer]
PublicKey = ${SPUB}
PresharedKey = ${PSK}
Endpoint = ${PUBIP}:${PORT}
AllowedIPs = 0.0.0.0/0, ::/0
PersistentKeepalive = 25
CLIENT
echo "=========== КОНЕЦ КОНФИГА ==========="
echo
echo "Готово: устройство «${NAME}» добавлено (${SUBNET}.${N})."
