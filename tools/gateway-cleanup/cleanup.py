#!/usr/bin/env python3
"""Удаляет аккаунты Gateway Plus, которые не заходили полгода.

База — отдельный проект Firebase vpn0dns (узел gatewayAccounts). Клиентам правила не дают ничего удалять, поэтому
удаляет эта задача токеном сервисного аккаунта (секрет репозитория VPN_FIREBASE_SERVICE_ACCOUNT).

Правила:
  * аккаунт считается неактивным, если и lastSeen, и createdAt старше DAYS (по умолчанию 183 дня ≈ полгода);
  * у аккаунта нет дат вообще / запись повреждена / номер не из 24 hex — НЕ трогаем;
  * заблокированные (есть gatewayBlocks/<номер>) — НЕ трогаем: нужна память о блокировке и о том, кто это;
  * gatewayBlocks и gatewayBlockedDevices не удаляются никогда;
  * защита от ошибки: если удалять пришлось бы больше max(25, половины всех аккаунтов) — остановиться (--force снимает);
  * в журнал пишутся только числа: имена и номера аккаунтов в журнал (он публичный) не попадают.
Человек, чей аккаунт удалён, ничего не теряет: плагин для ПК и приложение для Android сами заводят запись заново
при ближайшем «пульсе» (ключ и имя лежат у человека), серверы и профили в базе не хранятся.
"""
import argparse
import base64
import json
import math
import os
import re
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request

DB_URL = "https://vpn0dns-default-rtdb.europe-west1.firebasedatabase.app"
ID_RE = re.compile(r"^[0-9a-f]{24}$")
DAY_MS = 86_400_000
DEFAULT_DAYS = 183
BATCH = 100
SCOPES = "https://www.googleapis.com/auth/firebase.database https://www.googleapis.com/auth/userinfo.email"


def is_num(v):
    return isinstance(v, (int, float)) and not isinstance(v, bool) and math.isfinite(v)


def b64url(b):
    return base64.urlsafe_b64encode(b).rstrip(b"=").decode()


def mint_token(sa, now=None):
    """OAuth2-токен из ключа сервисного аккаунта (JWT RS256, подпись делает openssl — без сторонних библиотек)."""
    now = int(now if now is not None else time.time())
    uri = sa.get("token_uri") or "https://oauth2.googleapis.com/token"
    head = b64url(json.dumps({"alg": "RS256", "typ": "JWT"}).encode())
    claim = b64url(json.dumps({"iss": sa["client_email"], "scope": SCOPES, "aud": uri, "iat": now, "exp": now + 3000}).encode())
    signing_input = f"{head}.{claim}".encode()
    fd, key_path = tempfile.mkstemp(prefix="gw-key-")
    try:
        os.fchmod(fd, 0o600)
        with os.fdopen(fd, "w") as f:
            f.write(sa["private_key"])
        sig = subprocess.run(["openssl", "dgst", "-sha256", "-sign", key_path], input=signing_input,
                             capture_output=True, check=True).stdout
    finally:
        try:
            os.unlink(key_path)
        except OSError:
            pass
    jwt = f"{head}.{claim}.{b64url(sig)}"
    data = urllib.parse.urlencode({"grant_type": "urn:ietf:params:oauth:grant-type:jwt-bearer", "assertion": jwt}).encode()
    with urllib.request.urlopen(urllib.request.Request(uri, data=data), timeout=30) as r:
        return json.load(r)["access_token"]


class Db:
    def __init__(self, url, token):
        host = urllib.parse.urlparse(url).hostname or ""
        # Только отдельная база Gateway Plus (или локальная подставная для проверок): в основной базе проекта такой узел тоже
        # есть (прежние записи), его эта задача трогать не должна.
        if url.rstrip("/") != DB_URL and host not in ("127.0.0.1", "localhost"):
            raise SystemExit("Отказ: адрес базы не vpn0dns.")
        self.url = url.rstrip("/")
        self.token = token

    def _req(self, method, path, query="", body=None):
        u = f"{self.url}/{path}.json" + (f"?{query}" if query else "")
        data = None if body is None else json.dumps(body).encode()
        headers = {"Content-Type": "application/json"}
        if self.token:
            headers["Authorization"] = f"Bearer {self.token}"
        req = urllib.request.Request(u, data=data, method=method, headers=headers)
        try:
            with urllib.request.urlopen(req, timeout=60) as r:
                return json.loads(r.read() or b"null")
        except urllib.error.HTTPError as e:
            # тело ответа не печатаем: оно может содержать данные
            raise SystemExit(f"База ответила HTTP {e.code} на {method} {path}")
        except urllib.error.URLError as e:
            raise SystemExit(f"Нет связи с базой: {e.reason}")

    def get(self, path, shallow=False):
        return self._req("GET", path, "shallow=true" if shallow else "")

    def update_root(self, updates):
        return self._req("PATCH", "", body=updates)


def plan(accounts, blocked, now_ms, days):
    """Возвращает (список номеров к удалению, счётчики по причинам)."""
    cutoff = now_ms - days * DAY_MS
    stale, stats = [], {"всего": 0, "активные": 0, "заблокированные (оставлены)": 0, "без дат (оставлены)": 0,
                        "повреждённые (оставлены)": 0, "к удалению": 0}
    for aid, rec in (accounts or {}).items():
        stats["всего"] += 1
        if not ID_RE.match(aid) or not isinstance(rec, dict):
            stats["повреждённые (оставлены)"] += 1
            continue
        dates = [v for v in (rec.get("lastSeen"), rec.get("createdAt")) if is_num(v)]
        if not dates:
            stats["без дат (оставлены)"] += 1
            continue
        if max(dates) >= cutoff:
            stats["активные"] += 1
            continue
        if aid in blocked:
            stats["заблокированные (оставлены)"] += 1
            continue
        stale.append(aid)
    stats["к удалению"] = len(stale)
    return stale, stats


def summary(lines):
    text = "\n".join(lines)
    print(text)
    path = os.environ.get("GITHUB_STEP_SUMMARY")
    if path:
        with open(path, "a", encoding="utf-8") as f:
            f.write("### Очистка неактивных аккаунтов Gateway Plus\n\n" + "\n".join(f"- {l}" for l in lines) + "\n")


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--db-url", default=DB_URL)
    ap.add_argument("--credentials", help="JSON-ключ сервисного аккаунта проекта vpn0dns")
    ap.add_argument("--token", help="готовый токен (для проверок)")
    ap.add_argument("--days", type=int, default=DEFAULT_DAYS)
    ap.add_argument("--now-ms", type=int, help="текущее время, мс (для проверок)")
    ap.add_argument("--dry-run", action="store_true", help="ничего не удалять, только посчитать")
    ap.add_argument("--force", action="store_true", help="не останавливаться из-за большого числа удаляемых")
    a = ap.parse_args(argv)
    if a.days < 90:
        raise SystemExit("Отказ: срок меньше 90 дней не допускается.")

    token = a.token
    if not token and a.credentials:
        with open(a.credentials, encoding="utf-8") as f:
            token = mint_token(json.load(f))
    if not token and urllib.parse.urlparse(a.db_url).hostname not in ("127.0.0.1", "localhost"):
        raise SystemExit("Нужен ключ сервисного аккаунта (--credentials).")

    db = Db(a.db_url, token)
    now_ms = a.now_ms if a.now_ms is not None else int(time.time() * 1000)
    accounts = db.get("gatewayAccounts")
    if accounts is not None and not isinstance(accounts, dict):
        raise SystemExit("Узел gatewayAccounts странного вида — остановлено.")
    blocked = set((db.get("gatewayBlocks", shallow=True) or {}).keys())
    stale, stats = plan(accounts, blocked, now_ms, a.days)

    lines = [f"{k}: {v}" for k, v in stats.items()]
    lines.append(f"срок неактивности: {a.days} дн.")
    limit = max(25, math.ceil(stats["всего"] * 0.5))
    if len(stale) > limit and not a.force:
        lines.append(f"ОСТАНОВЛЕНО: к удалению {len(stale)} — больше допустимых {limit}; ничего не удалено (запуск вручную с force).")
        summary(lines)
        return 2
    if a.dry_run or not stale:
        lines.append("пробный запуск — ничего не удалено" if a.dry_run and stale else "удалять нечего")
        summary(lines)
        return 0

    for i in range(0, len(stale), BATCH):
        db.update_root({f"gatewayAccounts/{aid}": None for aid in stale[i:i + BATCH]})
    left = set((db.get("gatewayAccounts", shallow=True) or {}).keys()) & set(stale)
    if left:
        lines.append(f"ОШИБКА: после удаления осталось {len(left)} из {len(stale)}")
        summary(lines)
        return 1
    lines.append(f"удалено: {len(stale)}")
    summary(lines)
    return 0


if __name__ == "__main__":
    sys.exit(main())
