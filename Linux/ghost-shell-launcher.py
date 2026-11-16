#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Ghost Shell Launcher для Linux.

Ставит, обновляет, запускает и удаляет ОДНО приложение — Ghost Shell Terminal (админ-панель проекта,
Electron-окно вокруг живой страницы). Это то же, что делает Windows-лаунчер, только для Linux и только
с этим приложением.

Устройство (кратко; подробности — CLAUDE.md репозитория, раздел «Лаунчер для Linux»):
- Только стандартная библиотека Python 3.8+. Окно — GTK 3 (есть на Ubuntu/Debian/Mint с рабочим столом);
  нет GTK или нет экрана — работает текстовый режим (та же логика, команды ниже).
- Версию приложения узнаём ПРЯМЫМИ адресами GitHub (переадресация /releases/latest и файл latest-linux.yml),
  не через api.github.com — у API лимит 60 запросов в час на адрес.
- Скачанный файл проверяется по sha512 из latest-linux.yml; нет суммы или не совпала — не ставим.
- Всё лежит в домашней папке (~/.local/share/ghost-shell-launcher), права администратора не нужны.
"""
import base64
import hashlib
import json
import os
import re
import shutil
import ssl
import stat
import subprocess
import sys
import tarfile
import tempfile
import threading
import zipfile
import time
import urllib.error
import urllib.parse
import urllib.request

VERSION = "1.1.0"

APP = {
    "id": "terminal",
    "name": "Ghost Shell Terminal",
    "desc": "Админ-панель проекта: игра, ВидеоЧат, чаты Telegram, VPN. Вход — как обычно, подтверждением в Telegram.",
    "repo": "botsystemtioxsit/battle-admin-bot",
    "asset": re.compile(r"^battle-admin-desktop-(\d+\.\d+\.\d+)-x86_64\.AppImage$"),
    "icon": "ghost-shell-terminal",
}
LAUNCHER_REPO = "botsystemtioxsit/launcher"
LAUNCHER_TAG = "linux-latest"

# Адрес GitHub можно подменить ТОЛЬКО для проверок (локальный сервер вместо github.com).
_TEST_BASE = os.environ.get("GSL_TEST_BASE", "").rstrip("/")
GITHUB = _TEST_BASE or "https://github.com"
RAW = _TEST_BASE or "https://raw.githubusercontent.com"
OWNER = "botsystemtioxsit"
_TEST_HOST = urllib.parse.urlparse(_TEST_BASE).netloc if _TEST_BASE else ""


def data_dir():
    base = os.environ.get("GSL_HOME") or os.path.join(os.environ.get("XDG_DATA_HOME") or os.path.expanduser("~/.local/share"), "ghost-shell-launcher")
    return base


def paths():
    root = data_dir()
    home = os.path.expanduser("~")
    xdg = os.environ.get("XDG_DATA_HOME") or os.path.join(home, ".local/share")
    return {
        "root": root,
        "apps": os.path.join(root, "apps", APP["id"]),
        "state": os.path.join(root, "state.json"),
        "tmp": os.path.join(root, "tmp"),
        "log": os.path.join(root, "terminal.log"),
        "launcher": os.path.join(root, "launcher"),
        "bin": os.environ.get("GSL_BIN") or os.path.join(home, ".local/bin"),
        "applications": os.environ.get("GSL_APPLICATIONS") or os.path.join(xdg, "applications"),
        "icons": os.environ.get("GSL_ICONS") or os.path.join(xdg, "icons/hicolor/256x256/apps"),
    }


class LauncherError(Exception):
    """Ошибка, текст которой можно показать человеку как есть."""


# ---------------------------------------------------------------- сеть

_ALLOWED_HOSTS = ("github.com",)
_ALLOWED_SUFFIXES = (".github.com", ".githubusercontent.com")


def _host_ok(url):
    p = urllib.parse.urlparse(url)
    host = (p.hostname or "").lower()
    if _TEST_HOST and p.netloc == _TEST_HOST:
        return True
    if p.scheme != "https":
        return False
    return host in _ALLOWED_HOSTS or host.endswith(_ALLOWED_SUFFIXES)


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def _ua():
    return {"User-Agent": "GhostShellLauncher-Linux/" + VERSION}


def explain(err):
    """Понятная причина вместо технического текста."""
    if isinstance(err, LauncherError):
        return str(err)
    if isinstance(err, urllib.error.HTTPError):
        if err.code == 404:
            return "Файла нет в релизе (404). Возможно, релиз ещё публикуется — попробуйте через несколько минут."
        if err.code in (403, 429):
            return "GitHub временно отказал (слишком много запросов). Попробуйте через несколько минут."
        return "GitHub ответил ошибкой " + str(err.code) + "."
    if isinstance(err, (urllib.error.URLError, ssl.SSLError, ConnectionError)):
        return "Нет связи с GitHub. Проверьте интернет и попробуйте ещё раз."
    if isinstance(err, TimeoutError):
        return "GitHub не ответил вовремя. Попробуйте ещё раз."
    if isinstance(err, PermissionError):
        return "Нет права записи в папку установки: " + str(err)
    if isinstance(err, OSError) and getattr(err, "errno", None) == 28:
        return "Не хватает места на диске."
    return "Не получилось: " + str(err)


def http_get(url, timeout=30, max_bytes=4 * 1024 * 1024):
    if not _host_ok(url):
        raise LauncherError("Адрес не из GitHub — не открываю: " + url)
    req = urllib.request.Request(url, headers=_ua())
    last = None
    for attempt in range(3):
        try:
            with urllib.request.urlopen(req, timeout=timeout) as r:
                if not _host_ok(r.geturl()):
                    raise LauncherError("GitHub перенаправил на чужой адрес — не открываю.")
                data = r.read(max_bytes + 1)
                if len(data) > max_bytes:
                    raise LauncherError("Ответ слишком большой.")
                return data
        except urllib.error.HTTPError as e:
            if e.code in (404, 403, 429):
                raise
            last = e
        except (urllib.error.URLError, TimeoutError, ConnectionError, ssl.SSLError) as e:
            last = e
        time.sleep(1.5 * (attempt + 1))
    raise last


def latest_tag(repo):
    """Тег последнего выпуска: /releases/latest отвечает переадресацией на /releases/tag/<тег> (саму не выполняем)."""
    url = "%s/%s/releases/latest" % (GITHUB, repo)
    if not _host_ok(url):
        raise LauncherError("Адрес не из GitHub.")
    opener = urllib.request.build_opener(_NoRedirect)
    try:
        with opener.open(urllib.request.Request(url, headers=_ua()), timeout=30) as r:
            raise LauncherError("GitHub не отдал номер выпуска.")
    except urllib.error.HTTPError as e:
        if e.code not in (301, 302, 303, 307, 308):
            raise
        loc = e.headers.get("Location", "")
    m = re.search(r"/releases/tag/([^/?#]+)$", urllib.parse.urlparse(loc).path)
    if not m:
        raise LauncherError("GitHub не отдал номер выпуска.")
    tag = urllib.parse.unquote(m.group(1))
    if not re.fullmatch(r"v?\d+\.\d+\.\d+(?:[A-Za-z0-9._-]{0,20})?", tag):
        raise LauncherError("Странный номер выпуска — не доверяю.")
    return tag


def parse_latest_yml(text):
    """Минимальный разбор latest-linux.yml (electron-builder): version + файлы с sha512. Без внешних библиотек."""
    version = None
    files = []
    cur = None
    for raw in text.splitlines():
        line = raw.rstrip()
        m = re.match(r"^version:\s*['\"]?([^'\"\s]+)", line)
        if m:
            version = m.group(1)
            continue
        m = re.match(r"^\s*-\s+url:\s*['\"]?([^'\"]+?)['\"]?\s*$", line)
        if m:
            cur = {"url": m.group(1)}
            files.append(cur)
            continue
        if cur is not None:
            m = re.match(r"^\s+(sha512|size):\s*['\"]?([^'\"\s]+)", line)
            if m:
                cur[m.group(1)] = m.group(2)
                continue
            if re.match(r"^\S", line):
                cur = None
    return version, files


def find_release():
    """Что сейчас выпущено: {version, tag, name, url, sha512, size}."""
    repo = APP["repo"]
    tag = latest_tag(repo)
    base = "%s/%s/releases/download/%s/" % (GITHUB, repo, urllib.parse.quote(tag))
    text = http_get(base + "latest-linux.yml").decode("utf-8", "replace")
    version, files = parse_latest_yml(text)
    for f in files:
        name = os.path.basename(f["url"])
        m = APP["asset"].match(name)
        if not m:
            continue
        if "sha512" not in f:
            raise LauncherError("В описании выпуска нет контрольной суммы — не ставлю.")
        try:
            raw = base64.b64decode(f["sha512"], validate=True)
        except Exception:
            raise LauncherError("Контрольная сумма в описании выпуска повреждена.")
        if len(raw) != 64:
            raise LauncherError("Контрольная сумма в описании выпуска повреждена.")
        return {"version": m.group(1), "tag": tag, "name": name, "url": base + urllib.parse.quote(name), "sha512": f["sha512"], "size": int(f.get("size") or 0)}
    raise LauncherError("В выпуске нет файла AppImage для Linux x86_64.")


def version_key(v):
    return tuple(int(x) for x in re.findall(r"\d+", v or "0")[:4])


def download(url, dest, expect_sha512_b64, progress=None, expect_size=0):
    """Качает в dest, считает sha512, сверяет. Не совпало — файл удаляется."""
    if not _host_ok(url):
        raise LauncherError("Адрес не из GitHub — не качаю.")
    h = hashlib.sha512()
    req = urllib.request.Request(url, headers=_ua())
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            if not _host_ok(r.geturl()):
                raise LauncherError("GitHub перенаправил на чужой адрес — не качаю.")
            total = int(r.headers.get("Content-Length") or expect_size or 0)
            got = 0
            with open(dest, "wb") as f:
                while True:
                    chunk = r.read(256 * 1024)
                    if not chunk:
                        break
                    f.write(chunk)
                    h.update(chunk)
                    got += len(chunk)
                    if got > 600 * 1024 * 1024:
                        raise LauncherError("Файл слишком большой — остановил.")
                    if progress and total:
                        progress(min(99, int(got * 100 / total)))
        if base64.b64encode(h.digest()).decode() != expect_sha512_b64:
            raise LauncherError("Контрольная сумма скачанного файла не совпала — файл не установлен.")
    except BaseException:
        try:
            os.remove(dest)
        except OSError:
            pass
        raise
    if progress:
        progress(100)


# ---------------------------------------------------------------- состояние

def read_state():
    p = paths()["state"]
    try:
        with open(p, "r", encoding="utf-8") as f:
            s = json.load(f)
        if isinstance(s, dict) and re.fullmatch(r"\d+\.\d+\.\d+", str(s.get("version", ""))) and re.fullmatch(r"[A-Za-z0-9._-]{1,120}", str(s.get("file", ""))):
            return s
    except (OSError, ValueError):
        pass
    return None


def installed_file():
    s = read_state()
    if not s:
        return None
    f = os.path.join(paths()["apps"], s["file"])
    return f if os.path.isfile(f) else None


def write_state(s):
    p = paths()
    os.makedirs(p["root"], exist_ok=True)
    tmp = p["state"] + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(s, f, ensure_ascii=False)
    os.replace(tmp, p["state"])


def status():
    s = read_state()
    return {"installed": bool(s and installed_file()), "version": s["version"] if s else None}


# ---------------------------------------------------------------- ярлыки

def desktop_entry(name, comment, exec_line, icon, extra=""):
    return ("[Desktop Entry]\nType=Application\nName=%s\nComment=%s\nExec=%s\nIcon=%s\nTerminal=false\nCategories=Utility;\n%s"
            % (name, comment, exec_line, icon, extra))


def _exec_quote(path):
    # в Exec= пробелы и спецсимволы берутся в двойные кавычки; % удваивается
    return '"' + path.replace("\\", "\\\\").replace('"', '\\"').replace("$", "\\$").replace("`", "\\`").replace("%", "%%") + '"'


def write_terminal_shortcut(app_file):
    p = paths()
    os.makedirs(p["applications"], exist_ok=True)
    launcher = os.path.join(p["bin"], "ghost-shell-launcher")
    exec_line = "%s run" % _exec_quote(launcher if os.path.exists(launcher) else os.path.abspath(sys.argv[0]))
    with open(os.path.join(p["applications"], "ghost-shell-terminal.desktop"), "w", encoding="utf-8") as f:
        f.write(desktop_entry(APP["name"], "Админ-панель проекта", exec_line, APP["icon"], "StartupWMClass=Ghost Shell Terminal\n"))
    _copy_icon("ghost-shell-terminal.png")


def _icon_sources():
    here = os.path.dirname(os.path.abspath(__file__))
    return [os.path.join(here, "icons"), os.path.join(paths()["launcher"], "icons"), here]


def _copy_icon(name):
    p = paths()
    for d in _icon_sources():
        src = os.path.join(d, name)
        if os.path.isfile(src):
            os.makedirs(p["icons"], exist_ok=True)
            shutil.copyfile(src, os.path.join(p["icons"], name))
            return True
    return False


def remove_terminal_shortcut():
    p = paths()
    for f in (os.path.join(p["applications"], "ghost-shell-terminal.desktop"), os.path.join(p["icons"], "ghost-shell-terminal.png")):
        try:
            os.remove(f)
        except OSError:
            pass


def _refresh_desktop_db():
    try:
        subprocess.run(["update-desktop-database", paths()["applications"]], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=10)
    except Exception:
        pass


# ---------------------------------------------------------------- установка / запуск / удаление

def check_update():
    """Возвращает (текущая, новая|None, release)."""
    s = read_state()
    cur = s["version"] if s else None
    rel = find_release()
    newer = rel["version"] if (cur is None or version_key(rel["version"]) > version_key(cur)) else None
    return cur, newer, rel


def install_terminal(progress=None, note=None):
    """Ставит (или обновляет) Ghost Shell Terminal. Возвращает установленную версию."""
    note = note or (lambda t: None)
    p = paths()
    if platform_machine() not in ("x86_64", "amd64"):
        raise LauncherError("Сейчас приложение есть только для 64-разрядных Linux на Intel/AMD (x86_64). У вас: %s." % platform_machine())
    note("Узнаю, что вышло…")
    rel = find_release()
    os.makedirs(p["tmp"], exist_ok=True)
    os.makedirs(p["apps"], exist_ok=True)
    tmpfile = os.path.join(p["tmp"], rel["name"] + ".part")
    note("Скачиваю версию %s…" % rel["version"])
    download(rel["url"], tmpfile, rel["sha512"], progress, rel["size"])
    note("Устанавливаю…")
    os.chmod(tmpfile, 0o755)
    final = os.path.join(p["apps"], rel["name"])
    os.replace(tmpfile, final)
    old = read_state()
    write_state({"version": rel["version"], "file": rel["name"], "tag": rel["tag"], "installedAt": int(time.time())})
    if old and old.get("file") and old["file"] != rel["name"]:
        try:
            os.remove(os.path.join(p["apps"], old["file"]))
        except OSError:
            pass
    write_terminal_shortcut(final)
    _refresh_desktop_db()
    return rel["version"]


def platform_machine():
    import platform
    return platform.machine()


def _has_fuse2():
    for d in ("/usr/lib/x86_64-linux-gnu", "/usr/lib64", "/usr/lib", "/lib/x86_64-linux-gnu", "/lib64"):
        if os.path.exists(os.path.join(d, "libfuse.so.2")):
            return True
    return False


def _userns_blocked():
    try:
        if open("/proc/sys/kernel/apparmor_restrict_unprivileged_userns").read().strip() == "1":
            return True
    except OSError:
        pass
    try:
        if open("/proc/sys/kernel/unprivileged_userns_clone").read().strip() == "0":
            return True
    except OSError:
        pass
    return False


def launch_command(app_file):
    """Как запускать: AppImage без FUSE — с распаковкой на лету; где ядро запрещает песочницу пользователям — с --no-sandbox."""
    env = dict(os.environ)
    cmd = [app_file]
    if not _has_fuse2():
        env["APPIMAGE_EXTRACT_AND_RUN"] = "1"
    if os.getuid() == 0 or _userns_blocked():  # от root Electron без этого не стартует; где ядро запрещает песочницу обычным пользователям — тоже
        cmd.append("--no-sandbox")
    return cmd, env


def run_terminal():
    f = installed_file()
    if not f:
        raise LauncherError("Ghost Shell Terminal не установлен. Сначала нажмите «Установить».")
    cmd, env = launch_command(f)
    p = paths()
    os.makedirs(p["root"], exist_ok=True)
    log = open(p["log"], "ab")
    proc = subprocess.Popen(cmd, env=env, stdin=subprocess.DEVNULL, stdout=log, stderr=log, start_new_session=True, close_fds=True)
    time.sleep(4)  # AppImage без FUSE сначала распаковывается — даём время упасть, если упадёт
    code = proc.poll()
    if code is not None and code != 0:
        raise LauncherError("Приложение закрылось сразу (код %s). Подробности — в файле %s" % (code, p["log"]))
    return True


def remove_terminal():
    p = paths()
    shutil.rmtree(p["apps"], ignore_errors=True)
    try:
        os.remove(p["state"])
    except OSError:
        pass
    remove_terminal_shortcut()
    _refresh_desktop_db()


# ---------------------------------------------------------------- сам лаунчер

def self_install():
    """Кладёт лаунчер в ~/.local/share/ghost-shell-launcher/launcher, команду ghost-shell-launcher в ~/.local/bin и ярлык в меню."""
    p = paths()
    here = os.path.dirname(os.path.abspath(__file__))
    dest = p["launcher"]
    if os.path.abspath(here) != os.path.abspath(dest):
        os.makedirs(dest, exist_ok=True)
        shutil.copyfile(os.path.abspath(__file__), os.path.join(dest, "ghost-shell-launcher.py"))
        os.chmod(os.path.join(dest, "ghost-shell-launcher.py"), 0o755)
        src_icons = os.path.join(here, "icons")
        if os.path.isdir(src_icons):
            shutil.rmtree(os.path.join(dest, "icons"), ignore_errors=True)
            shutil.copytree(src_icons, os.path.join(dest, "icons"))
    os.makedirs(p["bin"], exist_ok=True)
    wrapper = os.path.join(p["bin"], "ghost-shell-launcher")
    target = os.path.join(dest, "ghost-shell-launcher.py")
    with open(wrapper, "w", encoding="utf-8") as f:
        f.write('#!/bin/sh\nexec python3 %s "$@"\n' % _sh_quote(target))
    os.chmod(wrapper, 0o755)
    os.makedirs(p["applications"], exist_ok=True)
    with open(os.path.join(p["applications"], "ghost-shell-launcher.desktop"), "w", encoding="utf-8") as f:
        f.write(desktop_entry("Ghost Shell Launcher", "Установка и запуск Ghost Shell Terminal", _exec_quote(wrapper), "ghost-shell-launcher"))
    _copy_icon("ghost-shell-launcher.png")
    _refresh_desktop_db()
    return wrapper


def _sh_quote(s):
    return "'" + s.replace("'", "'\\''") + "'"


def launcher_latest():
    """(версия, адрес архива, sha256) последнего лаунчера — из файлов выпуска linux-latest."""
    base = "%s/%s/releases/download/%s/" % (GITHUB, LAUNCHER_REPO, LAUNCHER_TAG)
    ver = http_get(base + "VERSION").decode("utf-8", "replace").strip()
    if not re.fullmatch(r"\d+\.\d+\.\d+", ver):
        raise LauncherError("Номер версии лаунчера повреждён.")
    sha = http_get(base + "GhostShellLauncher-linux.tar.gz.sha256").decode("utf-8", "replace").split()[0].lower()
    if not re.fullmatch(r"[0-9a-f]{64}", sha):
        raise LauncherError("Контрольная сумма лаунчера повреждена.")
    return ver, base + "GhostShellLauncher-linux.tar.gz", sha


def check_launcher_update():
    ver, url, sha = launcher_latest()
    return (ver if version_key(ver) > version_key(VERSION) else None), ver, url, sha


def self_update(note=None):
    """Заменяет файлы лаунчера на новые (только если мы запущены из папки установки). Возвращает новую версию или None."""
    note = note or (lambda t: None)
    p = paths()
    here = os.path.dirname(os.path.abspath(__file__))
    if os.path.abspath(here) != os.path.abspath(p["launcher"]):
        raise LauncherError("Лаунчер запущен не из папки установки. Установите его командой: python3 ghost-shell-launcher.py install-self")
    newer, ver, url, sha = check_launcher_update()
    if not newer:
        return None
    note("Скачиваю лаунчер %s…" % ver)
    os.makedirs(p["tmp"], exist_ok=True)
    tgz = os.path.join(p["tmp"], "launcher.tar.gz")
    h = hashlib.sha256()
    req = urllib.request.Request(url, headers=_ua())
    with urllib.request.urlopen(req, timeout=60) as r, open(tgz, "wb") as f:
        if not _host_ok(r.geturl()):
            raise LauncherError("GitHub перенаправил на чужой адрес — не качаю.")
        n = 0
        while True:
            c = r.read(65536)
            if not c:
                break
            n += len(c)
            if n > 20 * 1024 * 1024:
                raise LauncherError("Архив лаунчера слишком большой.")
            h.update(c)
            f.write(c)
    if h.hexdigest() != sha:
        os.remove(tgz)
        raise LauncherError("Контрольная сумма лаунчера не совпала — не обновляю.")
    stage = tempfile.mkdtemp(dir=p["tmp"])
    try:
        safe_extract(tgz, stage)
        top = stage
        entries = os.listdir(stage)
        if len(entries) == 1 and os.path.isdir(os.path.join(stage, entries[0])):
            top = os.path.join(stage, entries[0])
        if not os.path.isfile(os.path.join(top, "ghost-shell-launcher.py")):
            raise LauncherError("В архиве лаунчера нет главного файла.")
        newpy = os.path.join(top, "ghost-shell-launcher.py")
        compile(open(newpy, "rb").read(), newpy, "exec")  # хотя бы читается как Python
        shutil.copyfile(newpy, os.path.join(p["launcher"], "ghost-shell-launcher.py.new"))
        os.chmod(os.path.join(p["launcher"], "ghost-shell-launcher.py.new"), 0o755)
        os.replace(os.path.join(p["launcher"], "ghost-shell-launcher.py.new"), os.path.join(p["launcher"], "ghost-shell-launcher.py"))
        if os.path.isdir(os.path.join(top, "icons")):
            shutil.rmtree(os.path.join(p["launcher"], "icons"), ignore_errors=True)
            shutil.copytree(os.path.join(top, "icons"), os.path.join(p["launcher"], "icons"))
    finally:
        shutil.rmtree(stage, ignore_errors=True)
        try:
            os.remove(tgz)
        except OSError:
            pass
    return ver


def safe_extract(tgz, dest, max_files=200, max_file=5 * 1024 * 1024):
    """Распаковка без выхода за папку: только обычные файлы и папки, без ссылок и абсолютных путей."""
    dest = os.path.realpath(dest)
    with tarfile.open(tgz, "r:gz") as t:
        members = t.getmembers()
        if len(members) > max_files:
            raise LauncherError("В архиве слишком много файлов.")
        for m in members:
            if not (m.isfile() or m.isdir()):
                raise LauncherError("В архиве лишнее (ссылки или особые файлы) — не распаковываю.")
            target = os.path.realpath(os.path.join(dest, m.name))
            if not (target == dest or target.startswith(dest + os.sep)):
                raise LauncherError("Архив пытается выйти за свою папку — не распаковываю.")
            if m.isfile() and m.size > max_file:
                raise LauncherError("В архиве слишком большой файл.")
        for m in members:
            m.mode = 0o755 if (m.isdir() or m.name.endswith(".py")) else 0o644
        try:
            t.extractall(dest, members=members, filter="data")  # Python 3.12+
        except TypeError:
            t.extractall(dest, members=members)


# ---------------------------------------------------------------- программы и расширения для Linux
# Лаунчер сам их не запускает и в систему не ставит: скачивает файл, проверяет сумму (если она есть в релизе) и раскладывает так, как
# для этого вида принято, а дальше говорит человеку точный следующий шаг (.deb ставится командой с sudo, расширение подключает сам браузер).

# Запасной список (если не удалось прочитать catalog.json): те же записи, что в catalog.json → downloads / browserPlugins.
EXTRAS_DEFAULT = [
    {"id": "solitaire-linux", "name": "Косынка", "desc": "Пасьянс «Косынка» (пакет .deb: Debian, Ubuntu, Linux Mint).", "repo": "game-linux", "tag": "latest", "asset": "solitaire-linux.deb"},
    {"id": "phonecast", "name": "Phonecast", "desc": "Экран Android-телефона на ноутбуке по USB, с управлением мышью и клавиатурой.", "repo": "Remote", "tag": "latest", "asset": "phonecast-linux.tar.gz"},
    {"id": "tab-unloader", "name": "Tab Unloader", "desc": "Расширение для Chrome: выгружает неактивные вкладки, когда мало памяти.", "repo": "sstr", "tag": "latest", "asset": "tab-unloader.zip"},
    {"id": "mask-plus", "name": "Mask Plus", "desc": "Расширение для Chrome, Edge, Brave: подменяет часовой пояс, язык и местоположение под выбранную страну, закрывает утечку адреса через WebRTC. IP не меняет.", "repo": "launcher", "tag": "latest", "asset": "MaskPlus-extension.zip"},
]
_NAME_RE = re.compile(r"^[A-Za-z0-9._-]{1,120}$")
_ID_RE = re.compile(r"^[a-z0-9-]{1,40}$")


def extra_kind(asset):
    a = asset.lower()
    if a.endswith(".deb"):
        return "deb"
    if a.endswith(".tar.gz") or a.endswith(".tgz"):
        return "tar"
    if a.endswith(".zip"):
        return "ext"
    return None


def _clean_extra(d, extra_zip=None):
    """Одна запись каталога → проверенный словарь или None. Всё из сети проверяется по шаблонам."""
    try:
        src = extra_zip or d
        item = {"id": str(d.get("id", "")), "name": str(d.get("name", ""))[:60], "desc": re.sub(r"\s+", " ", str(d.get("desc", ""))).strip()[:300],
                "repo": str(src.get("repo", "")), "tag": str(src.get("tag", "latest")), "asset": str(src.get("asset", ""))}
    except AttributeError:
        return None
    if not (_ID_RE.match(item["id"]) and item["name"] and all(_NAME_RE.match(item[k]) for k in ("repo", "tag", "asset"))):
        return None
    item["kind"] = extra_kind(item["asset"])
    return item if item["kind"] else None


def extras_catalog():
    """Список программ для Linux: из catalog.json (его читаем прямым адресом raw), при неудаче — запасной список."""
    items = []
    try:
        data = json.loads(http_get(RAW + "/" + OWNER + "/launcher/main/catalog.json", max_bytes=512 * 1024).decode("utf-8"))
        for d in data.get("downloads", []):
            if isinstance(d, dict) and "linux" in (d.get("os") or []):
                it = _clean_extra(d)
                if it:
                    items.append(it)
        for d in data.get("browserPlugins", []):
            if isinstance(d, dict) and isinstance(d.get("zip"), dict):
                it = _clean_extra(d, d["zip"])
                if it:
                    items.append(it)
    except Exception:
        items = []
    return items or [dict(i, kind=extra_kind(i["asset"])) for i in EXTRAS_DEFAULT]


def extra_url(item, suffix=""):
    if item["tag"] == "latest":
        return "%s/%s/%s/releases/latest/download/%s%s" % (GITHUB, OWNER, item["repo"], item["asset"], suffix)
    return "%s/%s/%s/releases/download/%s/%s%s" % (GITHUB, OWNER, item["repo"], item["tag"], item["asset"], suffix)


def extra_dir(item):
    sub = "browser" if item["kind"] == "ext" else "apps"
    return os.path.join(data_dir(), sub, item["id"])


def downloads_dir():
    d = os.path.expanduser("~/Downloads")
    if os.environ.get("GSL_HOME"):
        d = os.path.join(data_dir(), "downloads")
    os.makedirs(d, exist_ok=True)
    return d


def extra_state(item):
    """Что уже скачано: путь (папка для архивов и расширений, файл для .deb) или None."""
    p = os.path.join(downloads_dir(), item["asset"]) if item["kind"] == "deb" else extra_dir(item)
    return p if os.path.exists(p) else None


def _download_plain(url, dest, progress=None, max_bytes=300 * 1024 * 1024):
    if not _host_ok(url):
        raise LauncherError("Адрес не из GitHub — не качаю.")
    h = hashlib.sha256()
    req = urllib.request.Request(url, headers=_ua())
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            if not _host_ok(r.geturl()):
                raise LauncherError("GitHub перенаправил на чужой адрес — не качаю.")
            total = int(r.headers.get("Content-Length") or 0)
            got = 0
            with open(dest, "wb") as f:
                while True:
                    chunk = r.read(256 * 1024)
                    if not chunk:
                        break
                    f.write(chunk)
                    h.update(chunk)
                    got += len(chunk)
                    if got > max_bytes:
                        raise LauncherError("Файл слишком большой — остановил.")
                    if progress and total:
                        progress(min(99, int(got * 100 / total)))
    except BaseException:
        try:
            os.remove(dest)
        except OSError:
            pass
        raise
    return h.hexdigest()


_EXT_ALLOWED = (".js", ".json", ".html", ".css", ".png", ".jpg", ".jpeg", ".svg", ".ico", ".txt", ".md", ".woff", ".woff2")


def safe_extract_zip(zpath, dest):
    """Распаковка расширения: только разрешённые типы файлов, без выхода за папку, с лимитами."""
    dest = os.path.realpath(dest)
    with zipfile.ZipFile(zpath) as z:
        infos = z.infolist()
        if len(infos) > 3000:
            raise LauncherError("В архиве слишком много файлов.")
        total = 0
        for i in infos:
            if i.is_dir():
                continue
            name = i.filename
            if name.startswith("/") or ".." in name.replace("\\", "/").split("/") or not name.lower().endswith(_EXT_ALLOWED):
                raise LauncherError("В архиве расширения лишнее (%s) — не распаковываю." % name[:60])
            if i.file_size > 20 * 1024 * 1024:
                raise LauncherError("В архиве слишком большой файл.")
            total += i.file_size
            target = os.path.realpath(os.path.join(dest, name))
            if not target.startswith(dest + os.sep):
                raise LauncherError("Архив пытается выйти за свою папку — не распаковываю.")
        if total > 80 * 1024 * 1024:
            raise LauncherError("Архив слишком большой.")
        for i in infos:
            if i.is_dir():
                continue
            target = os.path.join(dest, i.filename)
            os.makedirs(os.path.dirname(target), exist_ok=True)
            with z.open(i) as src, open(target, "wb") as out:
                shutil.copyfileobj(src, out)
            os.chmod(target, 0o644)


def install_extra(item, progress=None, note=None):
    """Скачать и разложить. Возвращает { path, hint, verified }."""
    say = note or (lambda *_: None)
    os.makedirs(os.path.join(data_dir(), "tmp"), exist_ok=True)
    tmp = os.path.join(data_dir(), "tmp", item["id"] + "-" + str(os.getpid()) + ".part")
    say("Скачиваю %s…" % item["name"])
    digest = _download_plain(extra_url(item), tmp, progress)
    verified = False
    try:
        want = http_get(extra_url(item, ".sha256"), max_bytes=4096).decode("utf-8", "replace").split()
        if want:
            if want[0].lower() != digest:
                raise LauncherError("Контрольная сумма скачанного файла не совпала — файл не принят.")
            verified = True
    except urllib.error.HTTPError as e:
        if e.code not in (403, 404):
            os.remove(tmp)
            raise
    except LauncherError:
        os.remove(tmp)
        raise
    except Exception:
        pass  # суммы в релизе нет или не прочиталась — это сообщим человеку, но не мешаем
    try:
        kind = item["kind"]
        if kind == "deb":
            dest = os.path.join(downloads_dir(), item["asset"])
            shutil.move(tmp, dest)
            return {"path": dest, "verified": verified, "hint": "Установить (нужен пароль администратора):\n  sudo apt install %s" % _sh_quote(dest)}
        target = extra_dir(item)
        stage = target + ".new"
        shutil.rmtree(stage, ignore_errors=True)
        os.makedirs(stage)
        say("Распаковываю…")
        try:
            if kind == "tar":
                safe_extract(tmp, stage, max_files=2000, max_file=200 * 1024 * 1024)
            else:
                safe_extract_zip(tmp, stage)
                if not os.path.isfile(os.path.join(stage, "manifest.json")):
                    subs = [d for d in os.listdir(stage) if os.path.isdir(os.path.join(stage, d))]
                    if len(subs) == 1 and os.path.isfile(os.path.join(stage, subs[0], "manifest.json")):
                        inner = os.path.join(stage, subs[0])
                        for n in os.listdir(inner):
                            shutil.move(os.path.join(inner, n), os.path.join(stage, n))
                        os.rmdir(inner)
                    else:
                        raise LauncherError("В архиве нет файла manifest.json — это не расширение.")
        except BaseException:
            shutil.rmtree(stage, ignore_errors=True)
            raise
        shutil.rmtree(target, ignore_errors=True)
        os.replace(stage, target)
        if kind == "tar":
            inst = next((os.path.join(r, "install.sh") for r, _, fs in os.walk(target) if "install.sh" in fs), None)
            hint = ("Установить:\n  cd %s && sh ./install.sh" % _sh_quote(os.path.dirname(inst))) if inst else "Папка с программой: " + target
        else:
            hint = ("Подключить в браузере: откройте chrome://extensions, включите «Режим разработчика», нажмите «Загрузить распакованное расширение» "
                    "и выберите папку:\n  %s\nПосле обновления нажмите круглую стрелку у расширения." % target)
        return {"path": target, "verified": verified, "hint": hint}
    finally:
        try:
            os.remove(tmp)
        except OSError:
            pass


def remove_extra(item):
    p = extra_state(item)
    if not p:
        return False
    if os.path.isdir(p):
        shutil.rmtree(p, ignore_errors=True)
    else:
        os.remove(p)
    return True


def find_extra(cat, key):
    key = (key or "").lower()
    return next((i for i in cat if i["id"] == key), None)


# ---------------------------------------------------------------- текстовый режим

def cli(argv):
    cmd = argv[0] if argv else ""

    def say(t):
        print(t, flush=True)

    def bar(pct):
        sys.stdout.write("\r  %3d%%" % pct)
        if pct >= 100:
            sys.stdout.write("\n")
        sys.stdout.flush()

    try:
        if cmd == "status":
            st = status()
            say("Ghost Shell Launcher %s" % VERSION)
            say("%s: %s" % (APP["name"], ("установлен, версия " + st["version"]) if st["installed"] else "не установлен"))
        elif cmd in ("install", "update"):
            cur, newer, rel = check_update() if cmd == "update" else (None, True, find_release())
            if cmd == "update" and not newer:
                say("Уже последняя версия (%s)." % cur)
                return 0
            v = install_terminal(bar, say)
            say("Готово: %s %s." % (APP["name"], v))
        elif cmd == "check":
            cur, newer, rel = check_update()
            say("Установлена: %s · вышла: %s%s" % (cur or "нет", rel["version"], " — есть обновление" if newer and cur else ""))
        elif cmd == "list":
            for it in extras_catalog():
                st = extra_state(it)
                say("%-16s %-14s %s%s" % (it["id"], it["name"], {"deb": "пакет .deb", "tar": "программа", "ext": "расширение для браузера"}[it["kind"]], " — скачано" if st else ""))
        elif cmd == "get":
            it = find_extra(extras_catalog(), argv[1] if len(argv) > 1 else "")
            if not it:
                say("Нет такой программы. Список: ghost-shell-launcher list")
                return 2
            r = install_extra(it, bar, say)
            say("Готово: " + r["path"] + ("" if r["verified"] else "\n(контрольной суммы в релизе нет — файл не сверялся)"))
            say(r["hint"])
        elif cmd == "drop":
            it = find_extra(extras_catalog(), argv[1] if len(argv) > 1 else "")
            say("Удалено." if it and remove_extra(it) else "Нечего удалять.")
        elif cmd == "run":
            run_terminal()
        elif cmd in ("remove", "uninstall"):
            remove_terminal()
            say("Удалено.")
        elif cmd == "install-self":
            w = self_install()
            say("Лаунчер установлен: команда %s и ярлык в меню приложений." % w)
        elif cmd == "self-update":
            v = self_update(say)
            say("Лаунчер обновлён до %s." % v if v else "Лаунчер последней версии (%s)." % VERSION)
        elif cmd in ("version", "--version", "-V"):
            say(VERSION)
        elif cmd in ("help", "--help", "-h"):
            say(USAGE)
        else:
            say(USAGE)
            return 2
        return 0
    except KeyboardInterrupt:
        return 130
    except Exception as e:  # человеку — понятная причина, а не трассировка
        print("Ошибка: " + explain(e), file=sys.stderr)
        if os.environ.get("GSL_DEBUG"):
            raise
        return 1


USAGE = """Ghost Shell Launcher %s (Linux)

  ghost-shell-launcher            окно (если есть экран и GTK), иначе эта подсказка
  ghost-shell-launcher status     что установлено
  ghost-shell-launcher install    установить Ghost Shell Terminal
  ghost-shell-launcher update     обновить, если вышла новая версия
  ghost-shell-launcher check      узнать, что вышло
  ghost-shell-launcher run        запустить
  ghost-shell-launcher remove     удалить приложение (лаунчер остаётся)
  ghost-shell-launcher list       программы и расширения для Linux (Косынка, Phonecast, Tab Unloader, Mask Plus)
  ghost-shell-launcher get <имя>  скачать и разложить (имя — из списка), в конце подскажет, что делать дальше
  ghost-shell-launcher drop <имя> убрать скачанное
  ghost-shell-launcher install-self   поставить сам лаунчер (команда + ярлык в меню)
  ghost-shell-launcher self-update    обновить сам лаунчер
""" % VERSION


# ---------------------------------------------------------------- окно (GTK 3)

CSS = b"""
window { background-color: #070314; }
.card { background-color: #0d0822; border: 1px solid #4A22A0; padding: 18px; }
label { color: #EAF8FF; }
.muted { color: #A08FE0; }
.title { color: #00F0FF; font-family: monospace; font-size: 20px; font-weight: bold; }
.name { color: #EAF8FF; font-family: monospace; font-size: 17px; font-weight: bold; }
.ok { color: #3CFF9A; }
.warn { color: #FCEE0A; }
.bad { color: #FF3B5C; }
button { background-image: none; background-color: #0d0822; color: #00F0FF; border: 1px solid #00F0FF; border-radius: 3px; padding: 8px 16px; font-family: monospace; }
button:hover { background-color: #14103a; border-color: #FF2A9D; color: #FF2A9D; }
button:disabled { color: #4A22A0; border-color: #4A22A0; }
button.primary { background-color: #00F0FF; color: #070314; font-weight: bold; }
button.primary:hover { background-color: #FF2A9D; color: #070314; border-color: #FF2A9D; }
button.primary:disabled { background-color: #1a1442; color: #4A22A0; }
progressbar trough { background-color: #14103a; min-height: 8px; border-radius: 0; }
progressbar progress { background-color: #00F0FF; min-height: 8px; border-radius: 0; }
"""


def run_gui(on_ready=None):
    import gi
    gi.require_version("Gtk", "3.0")
    from gi.repository import Gtk, GLib, Gdk, GdkPixbuf

    class Win(Gtk.Window):
        def __init__(self):
            super().__init__(title="Ghost Shell Launcher")
            self.set_default_size(600, 700)
            self.set_resizable(True)
            self.set_border_width(0)
            self.busy = False
            self.release = None
            icon = next((os.path.join(d, "ghost-shell-launcher.png") for d in _icon_sources() if os.path.isfile(os.path.join(d, "ghost-shell-launcher.png"))), None)
            if icon:
                try:
                    self.set_icon_from_file(icon)
                except Exception:
                    pass
            prov = Gtk.CssProvider()
            prov.load_from_data(CSS)
            Gtk.StyleContext.add_provider_for_screen(Gdk.Screen.get_default(), prov, Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION)

            outer = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=14)
            outer.set_border_width(20)
            self.add(outer)

            head = Gtk.Label(label="GHOST SHELL LAUNCHER")
            head.get_style_context().add_class("title")
            head.set_xalign(0)
            outer.pack_start(head, False, False, 0)
            sub = Gtk.Label(label="Установка и запуск Ghost Shell Terminal на этом компьютере. Создано нейросетью.")
            sub.get_style_context().add_class("muted")
            sub.set_xalign(0)
            sub.set_line_wrap(True)
            outer.pack_start(sub, False, False, 0)

            card = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=16)
            card.get_style_context().add_class("card")
            outer.pack_start(card, False, False, 0)
            ip = next((os.path.join(d, "ghost-shell-terminal.png") for d in _icon_sources() if os.path.isfile(os.path.join(d, "ghost-shell-terminal.png"))), None)
            if ip:
                try:
                    pb = GdkPixbuf.Pixbuf.new_from_file_at_size(ip, 72, 72)
                    card.pack_start(Gtk.Image.new_from_pixbuf(pb), False, False, 0)
                except Exception:
                    pass
            col = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=6)
            card.pack_start(col, True, True, 0)
            name = Gtk.Label(label=APP["name"])
            name.get_style_context().add_class("name")
            name.set_xalign(0)
            col.pack_start(name, False, False, 0)
            desc = Gtk.Label(label=APP["desc"])
            desc.get_style_context().add_class("muted")
            desc.set_xalign(0)
            desc.set_line_wrap(True)
            desc.set_max_width_chars(46)
            col.pack_start(desc, False, False, 0)
            self.state_lbl = Gtk.Label(label="Проверяю…")
            self.state_lbl.set_xalign(0)
            self.state_lbl.set_line_wrap(True)
            self.state_lbl.set_max_width_chars(46)
            col.pack_start(self.state_lbl, False, False, 4)
            self.bar = Gtk.ProgressBar()
            col.pack_start(self.bar, False, False, 0)
            btns = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
            col.pack_start(btns, False, False, 6)
            self.b_main = Gtk.Button(label="Установить")
            self.b_main.get_style_context().add_class("primary")
            self.b_main.connect("clicked", lambda *_: self.on_main())
            self.b_run = Gtk.Button(label="Запустить")
            self.b_run.connect("clicked", lambda *_: self.on_run())
            self.b_del = Gtk.Button(label="Удалить")
            self.b_del.connect("clicked", lambda *_: self.on_remove())
            for b in (self.b_main, self.b_run, self.b_del):
                btns.pack_start(b, False, False, 0)

            eh = Gtk.Label(label="Программы и расширения для Linux")
            eh.get_style_context().add_class("name")
            eh.set_xalign(0)
            outer.pack_start(eh, False, False, 0)
            self.extras_box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=8)
            sw = Gtk.ScrolledWindow()
            sw.set_policy(Gtk.PolicyType.NEVER, Gtk.PolicyType.AUTOMATIC)
            sw.set_min_content_height(230)
            sw.add(self.extras_box)
            outer.pack_start(sw, True, True, 0)
            self.extras_box.pack_start(self._muted("Загружаю список…"), False, False, 0)
            self.extra_rows = {}

            foot = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=10)
            outer.pack_start(foot, False, False, 0)
            self.foot_lbl = Gtk.Label(label="Лаунчер %s" % VERSION)
            self.foot_lbl.get_style_context().add_class("muted")
            self.foot_lbl.set_xalign(0)
            foot.pack_start(self.foot_lbl, True, True, 0)
            self.b_self = Gtk.Button(label="Проверить обновление лаунчера")
            self.b_self.connect("clicked", lambda *_: self.on_self())
            foot.pack_end(self.b_self, False, False, 0)

            self.connect("destroy", Gtk.main_quit)
            self.refresh_state(check_net=True)
            self.load_extras()

        # --- программы и расширения (скачать и разложить; ничего не запускается и не ставится в систему без вашего шага)
        def _muted(self, text):
            l = Gtk.Label(label=text)
            l.get_style_context().add_class("muted")
            l.set_xalign(0)
            l.set_line_wrap(True)
            return l

        def load_extras(self, catalog=None):
            def work():
                cat = catalog if catalog is not None else extras_catalog()
                GLib.idle_add(lambda: (self.build_extras(cat), False)[1])
            threading.Thread(target=work, daemon=True).start()

        def build_extras(self, cat):
            for c in self.extras_box.get_children():
                self.extras_box.remove(c)
            self.extra_rows = {}
            for it in cat:
                row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=12)
                row.get_style_context().add_class("card")
                col = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=3)
                row.pack_start(col, True, True, 0)
                nm = Gtk.Label(label=it["name"] + "  ·  " + {"deb": "пакет .deb", "tar": "программа", "ext": "расширение для браузера"}[it["kind"]])
                nm.get_style_context().add_class("name")
                nm.set_xalign(0)
                nm.set_line_wrap(True)
                col.pack_start(nm, False, False, 0)
                col.pack_start(self._muted(it["desc"]), False, False, 0)
                st = Gtk.Label(label="")
                st.set_xalign(0)
                st.set_line_wrap(True)
                st.set_selectable(True)
                col.pack_start(st, False, False, 2)
                btns = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=6)
                b_get = Gtk.Button(label="Скачать")
                b_get.get_style_context().add_class("primary")
                b_open = Gtk.Button(label="Открыть папку")
                b_del = Gtk.Button(label="Убрать")
                for b in (b_get, b_open, b_del):
                    btns.pack_start(b, False, False, 0)
                row.pack_end(btns, False, False, 0)
                b_get.connect("clicked", lambda _b, it=it: self.on_extra_get(it))
                b_open.connect("clicked", lambda _b, it=it: self.on_extra_open(it))
                b_del.connect("clicked", lambda _b, it=it: self.on_extra_drop(it))
                self.extra_rows[it["id"]] = {"st": st, "get": b_get, "open": b_open, "del": b_del}
                self.extras_box.pack_start(row, False, False, 0)
                self.refresh_extra(it)
            self.extras_box.show_all()
            for it in cat:
                self.refresh_extra(it)

        def refresh_extra(self, it, text=None, cls=None):
            r = self.extra_rows.get(it["id"])
            if not r:
                return
            have = extra_state(it)
            ctx = r["st"].get_style_context()
            for c in ("ok", "warn", "bad"):
                ctx.remove_class(c)
            if cls:
                ctx.add_class(cls)
            r["st"].set_text(text if text is not None else ("Скачано: " + have if have else ""))
            r["get"].set_label("Скачать заново" if have else "Скачать")
            r["open"].set_visible(bool(have))
            r["open"].set_no_show_all(not have)
            r["del"].set_visible(bool(have))
            r["del"].set_no_show_all(not have)

        def on_extra_get(self, it):
            rw = self.extra_rows[it["id"]]
            rw["get"].set_sensitive(False)

            def work():
                return install_extra(it, None, lambda m: GLib.idle_add(lambda: (self.refresh_extra(it, m), False)[1]))

            def run():
                try:
                    res, err = work(), None
                except Exception as e:
                    res, err = None, e

                def done():
                    rw["get"].set_sensitive(True)
                    if err:
                        self.refresh_extra(it, "Не вышло: " + explain(err), "bad")
                    else:
                        self.refresh_extra(it, res["hint"] + ("" if res["verified"] else "\n(контрольной суммы в релизе нет — файл не сверялся)"), "ok")
                    return False
                GLib.idle_add(done)
            threading.Thread(target=run, daemon=True).start()

        def on_extra_open(self, it):
            p = extra_state(it)
            if p:
                try:
                    subprocess.Popen(["xdg-open", p if os.path.isdir(p) else os.path.dirname(p)], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
                except OSError:
                    self.refresh_extra(it, "Не получилось открыть папку: " + p, "warn")

        def on_extra_drop(self, it):
            remove_extra(it)
            self.refresh_extra(it, "Убрано.", None)

        # --- помощники
        def say(self, text, cls=None):
            ctx = self.state_lbl.get_style_context()
            for c in ("ok", "warn", "bad"):
                ctx.remove_class(c)
            if cls:
                ctx.add_class(cls)
            self.state_lbl.set_text(text)

        def set_busy(self, busy):
            self.busy = busy
            inst = status()["installed"]
            self.b_main.set_sensitive(not busy)
            self.b_self.set_sensitive(not busy)
            self.b_run.set_sensitive(inst and not busy)
            self.b_del.set_sensitive(inst and not busy)

        def bg(self, work, done):
            """Работа в потоке, результат — в потоке окна."""
            self.set_busy(True)

            def run():
                try:
                    res = (work(), None)
                except Exception as e:
                    res = (None, e)
                GLib.idle_add(lambda: (self.set_busy(False), done(*res), False)[2])
            threading.Thread(target=run, daemon=True).start()

        def progress(self, pct):
            GLib.idle_add(lambda: (self.bar.set_fraction(pct / 100.0), False)[1])

        def note(self, text):
            GLib.idle_add(lambda: (self.say(text), False)[1])

        # --- состояние
        def refresh_state(self, check_net=False):
            st = status()
            self.bar.set_fraction(0)
            if st["installed"]:
                self.say("Установлено: версия %s." % st["version"], "ok")
                self.b_main.set_label("Обновить")
                self.b_run.set_sensitive(True)
                self.b_del.set_sensitive(True)
            else:
                self.say("Не установлено.", None)
                self.b_main.set_label("Установить")
                self.b_run.set_sensitive(False)
                self.b_del.set_sensitive(False)
            if check_net:
                def done(res, err):
                    if err:
                        self.say(self.state_lbl.get_text() + "\nНе удалось проверить обновления: " + explain(err), "warn")
                        return
                    cur, newer, rel = res
                    self.release = rel
                    if cur and newer:
                        self.say("Установлено: %s. Вышла новая версия: %s." % (cur, newer), "warn")
                    elif cur:
                        self.say("Установлено: версия %s. Это последняя." % cur, "ok")
                    else:
                        self.say("Не установлено. Доступна версия %s." % rel["version"], None)
                self.bg(check_update, done)

        # --- действия
        def on_main(self):
            self.bar.set_fraction(0)

            def done(res, err):
                if err:
                    self.say("Не вышло: " + explain(err), "bad")
                else:
                    self.refresh_state()
                    self.say("Готово: версия %s. Нажмите «Запустить»." % res, "ok")
            self.bg(lambda: install_terminal(self.progress, self.note), done)

        def on_run(self):
            def done(res, err):
                if err:
                    self.say(explain(err), "bad")
                else:
                    self.say("Запущено. Окно приложения откроется через несколько секунд.", "ok")
            self.bg(run_terminal, done)

        def on_remove(self):
            d = Gtk.MessageDialog(transient_for=self, modal=True, message_type=Gtk.MessageType.QUESTION, buttons=Gtk.ButtonsType.YES_NO,
                                  text="Удалить Ghost Shell Terminal с этого компьютера?")
            d.format_secondary_text("Сам лаунчер останется. Ваш аккаунт и данные в проекте не затрагиваются.")
            r = d.run()
            d.destroy()
            if r != Gtk.ResponseType.YES:
                return

            def done(res, err):
                self.refresh_state()
                self.say("Удалено." if not err else "Не вышло: " + explain(err), "ok" if not err else "bad")
            self.bg(remove_terminal, done)

        def on_self(self):
            def work():
                newer, ver, url, sha = check_launcher_update()
                if not newer:
                    return ("none", ver)
                if os.path.abspath(os.path.dirname(os.path.abspath(__file__))) != os.path.abspath(paths()["launcher"]):
                    return ("manual", ver)
                return ("ok", self_update(self.note))

            def done(res, err):
                if err:
                    self.say("Не вышло: " + explain(err), "bad")
                    return
                kind, ver = res
                if kind == "none":
                    self.foot_lbl.set_text("Лаунчер %s — это последняя версия." % VERSION)
                elif kind == "manual":
                    self.foot_lbl.set_text("Вышел лаунчер %s. Обновите его: ghost-shell-launcher install-self (из новой папки)." % ver)
                else:
                    self.foot_lbl.set_text("Лаунчер обновлён до %s. Закройте и откройте его снова." % ver)
            self.bg(work, done)

    w = Win()
    w.show_all()
    if on_ready:  # только для проверок (снимки окна)
        on_ready(w, Gtk, GLib, Gdk)
    Gtk.main()
    return 0


def gui_available():
    if not (os.environ.get("DISPLAY") or os.environ.get("WAYLAND_DISPLAY")):
        return False
    try:
        import gi
        gi.require_version("Gtk", "3.0")
        from gi.repository import Gtk  # noqa: F401
        return True
    except Exception:
        return False


def main(argv):
    if argv:
        return cli(argv)
    if gui_available():
        try:
            return run_gui()
        except Exception as e:
            print("Окно не открылось (%s). Работаю в текстовом режиме.\n" % e, file=sys.stderr)
    print(USAGE)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
