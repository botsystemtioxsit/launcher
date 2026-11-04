#!/usr/bin/env bash
# Ghost Shell Gateway: проверяет и ускоряет УЖЕ настроенный WireGuard-сервер. Запускать от root.
# Что делает: (1) правило «подгонки размера пакетов» (частая причина «медленно/зависает»), сразу и навсегда;
# (2) очередь fq + алгоритм BBR, если ядро умеет; (3) печатает, как на самом деле работает сервер (ядро, процессор, скорость в интернет).
set -uo pipefail
if [ "$(id -u)" -ne 0 ]; then echo "Нужен root." >&2; exit 1; fi
if [ ! -f /etc/wireguard/wg0.conf ]; then echo "WireGuard на этом сервере не настроен мастером Gateway Plus (нет /etc/wireguard/wg0.conf)." >&2; exit 2; fi

RULE_ADD='iptables -t mangle -A FORWARD -p tcp --tcp-flags SYN,RST SYN -j TCPMSS --clamp-mss-to-pmtu'
RULE_DEL='iptables -t mangle -D FORWARD -p tcp --tcp-flags SYN,RST SYN -j TCPMSS --clamp-mss-to-pmtu'

# 1. подгонка размера пакетов (MSS): правило в wg0.conf (после перезагрузки) и сразу в работающем сервере
if grep -q 'TCPMSS' /etc/wireguard/wg0.conf; then
  echo "Размер пакетов: правило уже было."
else
  cp /etc/wireguard/wg0.conf /etc/wireguard/wg0.conf.bak-ghostshell
  sed -i "/^PostUp/ s|\$|; ${RULE_ADD}|" /etc/wireguard/wg0.conf
  sed -i "/^PostDown/ s|\$|; ${RULE_DEL}|" /etc/wireguard/wg0.conf
  echo "Размер пакетов: правило добавлено (копия настроек: wg0.conf.bak-ghostshell)."
fi
if ! iptables -t mangle -C FORWARD -p tcp --tcp-flags SYN,RST SYN -j TCPMSS --clamp-mss-to-pmtu 2>/dev/null; then
  iptables -t mangle -A FORWARD -p tcp --tcp-flags SYN,RST SYN -j TCPMSS --clamp-mss-to-pmtu && echo "Размер пакетов: правило включено сейчас."
fi

# 2. BBR
CC="$(cat /proc/sys/net/ipv4/tcp_congestion_control 2>/dev/null || echo ?)"
if [ "$CC" = "bbr" ]; then
  echo "Алгоритм BBR: уже включён."
elif modprobe tcp_bbr 2>/dev/null && grep -qw bbr /proc/sys/net/ipv4/tcp_available_congestion_control 2>/dev/null; then
  printf 'net.core.default_qdisc=fq\nnet.ipv4.tcp_congestion_control=bbr\n' > /etc/sysctl.d/99-ghostshell-net.conf
  sysctl --system > /dev/null 2>&1
  echo "Алгоритм BBR: включён (был $CC)."
else
  echo "Алгоритм BBR: ядро сервера его не поддерживает (остаётся $CC)."
fi

# 3. как работает сервер
echo "--- о сервере ---"
echo "Ядро: $(uname -r); процессоров: $(nproc); виртуализация: $(systemd-detect-virt 2>/dev/null || echo ?)"
if [ -d /sys/module/wireguard ] || modinfo wireguard > /dev/null 2>&1; then
  echo "WireGuard в ядре: да (быстрый режим)."
else
  echo "WireGuard в ядре: НЕТ — работает медленная программная версия. Выберите на хостинге другую систему/ядро."
fi
BYTES=30000000
SPEED="$(curl -4 -fsS --max-time 40 -o /dev/null -w '%{speed_download}' "https://speed.cloudflare.com/__down?bytes=${BYTES}" 2>/dev/null || echo 0)"
MBIT="$(awk -v s="${SPEED:-0}" 'BEGIN{printf "%d", s*8/1000000}')"
echo "Скорость самого сервера в интернет: ${MBIT} Мбит/с."
if [ "${MBIT:-0}" -lt 60 ]; then
  echo "ВЫВОД: медленный сам сервер или его канал (дешёвый тариф, перегружен). VPN не может быть быстрее сервера — попробуйте другой тариф или хостера."
else
  echo "ВЫВОД: сервер быстрый. Если VPN всё равно медленный — дело в дороге до сервера (мобильная сеть, далёкое расположение) или в MTU на устройстве."
fi
echo "Готово."
