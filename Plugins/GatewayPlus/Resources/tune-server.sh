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

# 3. быстрая пересылка UDP (ядро 6.2+): ускоряет WireGuard на «проходном» сервере; не умеет ядро или карта — тихо пропускаем
IFACE="$(ip -4 route show default | awk '{print $5; exit}')"
GRO_CMD="ethtool -K ${IFACE} rx-udp-gro-forwarding on rx-gro-list off"
if ! command -v ethtool > /dev/null 2>&1; then
  apt-get -o DPkg::Lock::Timeout=120 -y install ethtool > /dev/null 2>&1 || true
fi
if [ -z "$IFACE" ] || ! command -v ethtool > /dev/null 2>&1; then
  echo "Быстрая пересылка UDP: не получилось (нет ethtool или сетевого интерфейса) — пропускаю."
elif ! ethtool -k "$IFACE" 2>/dev/null | grep -q 'rx-udp-gro-forwarding'; then
  echo "Быстрая пересылка UDP: ядро сервера её не умеет (нужно 6.2 и новее) — пропускаю."
elif $GRO_CMD > /dev/null 2>&1; then
  if ! grep -q 'rx-udp-gro-forwarding' /etc/wireguard/wg0.conf; then
    cp -n /etc/wireguard/wg0.conf /etc/wireguard/wg0.conf.bak-ghostshell 2>/dev/null || true
    sed -i "/^ListenPort/a PostUp = ${GRO_CMD} >/dev/null 2>/dev/null || true" /etc/wireguard/wg0.conf
  fi
  echo "Быстрая пересылка UDP: включена."
else
  echo "Быстрая пересылка UDP: сетевая карта сервера не разрешила — пропускаю (не страшно)."
fi

# 4. IPv6 внутри туннеля. Без него у программ с IPv6 (почти все большие сайты) первая попытка уходит в тупик и страницы «тормозят».
# Включаем только если у сервера есть свой IPv6; иначе ничего не трогаем.
ULA=fd66:6761:7465::
if grep -q "${ULA}" /etc/wireguard/wg0.conf; then
  echo "IPv6 в туннеле: уже включён."
  echo "GS_IPV6_PREFIX=${ULA}"
else
  IFACE6="$(ip -6 route show default 2>/dev/null | awk '{print $5; exit}')"
  HAS6=0
  if [ -n "$IFACE6" ] && curl -6 -fsS --max-time 6 -o /dev/null https://api6.ipify.org 2>/dev/null; then HAS6=1; fi
  [ "${GS_FORCE_IPV6:-}" = "1" ] && HAS6=1   # только для проверки на тестовой машине
  [ -z "$IFACE6" ] && IFACE6="$IFACE"
  SADDR="$(awk -F' *= *' '/^Address/{print $2; exit}' /etc/wireguard/wg0.conf)"
  SUBNET="${SADDR%.*}"
  if [ "$HAS6" != "1" ]; then
    echo "IPv6 в туннеле: у сервера нет своего IPv6 — туннель остаётся только на IPv4 (программы сразу переходят на IPv4)."
  elif [ -z "$SUBNET" ]; then
    echo "IPv6 в туннеле: не получилось прочитать настройки сервера — пропускаю."
  else
    SUBQ="$(printf '%s' "$SUBNET" | sed 's/\./\\./g')"
    cp /etc/wireguard/wg0.conf /etc/wireguard/wg0.conf.bak-ghostshell-v6
    sed -i -E "s#^(Address = .*)\$#\1, ${ULA}1/64#" /etc/wireguard/wg0.conf
    sed -i -E "s#^(AllowedIPs = ${SUBQ}\.([0-9]+)/32)[[:space:]]*\$#\1, ${ULA}\2/128#" /etc/wireguard/wg0.conf
    sed -i "/^ListenPort/a PostDown = ip6tables -D FORWARD -i wg0 -j ACCEPT; ip6tables -D FORWARD -o wg0 -j ACCEPT; ip6tables -t nat -D POSTROUTING -o ${IFACE6} -j MASQUERADE; ip6tables -t mangle -D FORWARD -p tcp --tcp-flags SYN,RST SYN -j TCPMSS --clamp-mss-to-pmtu || true" /etc/wireguard/wg0.conf
    sed -i "/^ListenPort/a PostUp = ip6tables -A FORWARD -i wg0 -j ACCEPT; ip6tables -A FORWARD -o wg0 -j ACCEPT; ip6tables -t nat -A POSTROUTING -o ${IFACE6} -j MASQUERADE; ip6tables -t mangle -A FORWARD -p tcp --tcp-flags SYN,RST SYN -j TCPMSS --clamp-mss-to-pmtu || true" /etc/wireguard/wg0.conf
    echo 'net.ipv6.conf.all.forwarding=1' > /etc/sysctl.d/99-ghostshell-ipv6.conf
    sysctl -w net.ipv6.conf.all.forwarding=1 > /dev/null 2>&1 || true
    # перезапуск туннеля: на секунду-две подключённые устройства отвалятся и сами вернутся
    systemctl restart wg-quick@wg0 > /dev/null 2>&1 || true
    sleep 1
    if wg show wg0 > /dev/null 2>&1 && ip -6 addr show dev wg0 2>/dev/null | grep -q "${ULA}1"; then
      echo "IPv6 в туннеле: включён (у сервера есть свой IPv6). Профиль на компьютере плагин обновит сам."
      echo "GS_IPV6_PREFIX=${ULA}"
    else
      # что-то не сложилось — возвращаем как было, чтобы VPN продолжал работать
      cp /etc/wireguard/wg0.conf.bak-ghostshell-v6 /etc/wireguard/wg0.conf
      rm -f /etc/sysctl.d/99-ghostshell-ipv6.conf
      systemctl restart wg-quick@wg0 > /dev/null 2>&1 || true
      echo "IPv6 в туннеле: включить не получилось — настройки возвращены как были, VPN работает по-прежнему."
    fi
  fi
fi

# 5. как работает сервер
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
