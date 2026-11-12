#!/bin/sh
# Ставит Ghost Shell Launcher для Linux и (по желанию) сразу Ghost Shell Terminal.
#   curl -fsSL https://github.com/botsystemtioxsit/launcher/releases/download/linux-latest/install.sh | sh
# Права администратора не нужны: всё кладётся в домашнюю папку (~/.local). Проверяется контрольная сумма архива.
set -eu
BASE="https://github.com/botsystemtioxsit/launcher/releases/download/linux-latest"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

need() { command -v "$1" >/dev/null 2>&1 || { echo "Нужна программа «$1» — установите её (например: sudo apt install $1) и запустите ещё раз." >&2; exit 1; }; }
need python3
need curl
need tar
need sha256sum

echo "Скачиваю лаунчер…"
curl -fsSL "$BASE/GhostShellLauncher-linux.tar.gz" -o "$TMP/l.tar.gz"
curl -fsSL "$BASE/GhostShellLauncher-linux.tar.gz.sha256" -o "$TMP/l.sha256"
WANT="$(cut -d' ' -f1 "$TMP/l.sha256")"
GOT="$(sha256sum "$TMP/l.tar.gz" | cut -d' ' -f1)"
if [ "$WANT" != "$GOT" ]; then echo "Контрольная сумма не совпала — не устанавливаю." >&2; exit 1; fi

mkdir "$TMP/x"
tar -xzf "$TMP/l.tar.gz" -C "$TMP/x"
python3 "$TMP/x/ghost-shell-launcher.py" install-self

case ":$PATH:" in *":$HOME/.local/bin:"*) ;; *) echo "Подсказка: добавьте ~/.local/bin в PATH, чтобы вызывать команду ghost-shell-launcher из любого места." ;; esac
echo
echo "Готово. Лаунчер появился в меню приложений («Ghost Shell Launcher»)."
if [ -t 0 ] || [ -r /dev/tty ]; then
  printf "Поставить сразу Ghost Shell Terminal? [Y/n] "
  ANS="n"; { read ANS < /dev/tty; } 2>/dev/null || ANS="n"
  case "$ANS" in n|N|н|Н) echo "Хорошо. Откройте лаунчер и нажмите «Установить»." ;; *) "$HOME/.local/bin/ghost-shell-launcher" install ;; esac
fi
