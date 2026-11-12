# Проверки Linux-лаунчера без настоящего GitHub: локальный сервер изображает релизы.
# Запуск: python3 -m unittest discover -s tests/linux -v
import base64, hashlib, http.server, importlib.util, io, os, shutil, socketserver, sys, tarfile, tempfile, threading, unittest

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
SRC = os.path.join(ROOT, "Linux", "ghost-shell-launcher.py")


class Fake:
    """Что отдаёт «GitHub». Меняется между проверками."""
    version = "1.0.7"
    app_bytes = b"#!/bin/sh\necho fake-terminal \"$@\"\n"
    yml_override = None
    redirect_to = None
    launcher_version = "9.9.9"
    launcher_tar = b""
    launcher_sha = ""
    corrupt_app = False


def yml():
    if Fake.yml_override is not None:
        return Fake.yml_override
    sha = base64.b64encode(hashlib.sha512(Fake.app_bytes).digest()).decode()
    name = "battle-admin-desktop-%s-x86_64.AppImage" % Fake.version
    return ("version: %s\nfiles:\n  - url: %s\n    sha512: %s\n    size: %d\npath: %s\nsha512: %s\nreleaseDate: '2026-11-01T00:00:00.000Z'\n"
            % (Fake.version, name, sha, len(Fake.app_bytes), name, sha)).encode()


class H(http.server.BaseHTTPRequestHandler):
    def log_message(self, *a):
        pass

    def send(self, code, body=b"", headers=None):
        self.send_response(code)
        for k, v in (headers or {}).items():
            self.send_header(k, v)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        p = self.path
        if p == "/botsystemtioxsit/battle-admin-bot/releases/latest":
            return self.send(302, b"", {"Location": Fake.redirect_to or "/botsystemtioxsit/battle-admin-bot/releases/tag/v" + Fake.version})
        if p.endswith("/latest-linux.yml"):
            return self.send(200, yml())
        if p.endswith(".AppImage"):
            body = Fake.app_bytes[:-3] + b"XXX" if Fake.corrupt_app else Fake.app_bytes
            return self.send(200, body)
        if p == "/botsystemtioxsit/launcher/releases/download/linux-latest/VERSION":
            return self.send(200, Fake.launcher_version.encode())
        if p == "/botsystemtioxsit/launcher/releases/download/linux-latest/GhostShellLauncher-linux.tar.gz.sha256":
            return self.send(200, (Fake.launcher_sha + "  GhostShellLauncher-linux.tar.gz\n").encode())
        if p == "/botsystemtioxsit/launcher/releases/download/linux-latest/GhostShellLauncher-linux.tar.gz":
            return self.send(200, Fake.launcher_tar)
        self.send(404, b"no")


class Srv(socketserver.ThreadingMixIn, http.server.HTTPServer):
    daemon_threads = True


def load(env):
    for k, v in env.items():
        os.environ[k] = v
    spec = importlib.util.spec_from_file_location("gsl_%d" % id(env), SRC)
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


class T(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.srv = Srv(("127.0.0.1", 0), H)
        threading.Thread(target=cls.srv.serve_forever, daemon=True).start()
        cls.base = "http://127.0.0.1:%d" % cls.srv.server_address[1]

    @classmethod
    def tearDownClass(cls):
        cls.srv.shutdown()

    def setUp(self):
        self.tmp = tempfile.mkdtemp()
        self.env = {"GSL_TEST_BASE": self.base, "GSL_HOME": os.path.join(self.tmp, "home"), "GSL_BIN": os.path.join(self.tmp, "bin"),
                    "GSL_APPLICATIONS": os.path.join(self.tmp, "apps"), "GSL_ICONS": os.path.join(self.tmp, "icons")}
        self.m = load(self.env)
        Fake.version = "1.0.7"; Fake.app_bytes = b"#!/bin/sh\necho fake-terminal \"$@\"\n"; Fake.yml_override = None
        Fake.redirect_to = None; Fake.corrupt_app = False

    def tearDown(self):
        shutil.rmtree(self.tmp, ignore_errors=True)

    # --- разбор
    def test_yml_parse(self):
        v, files = self.m.parse_latest_yml(yml().decode())
        self.assertEqual(v, "1.0.7"); self.assertEqual(len(files), 1); self.assertIn("sha512", files[0])

    def test_version_compare(self):
        k = self.m.version_key
        self.assertTrue(k("1.0.10") > k("1.0.9")); self.assertTrue(k("2.0.0") > k("1.99.99")); self.assertEqual(k("1.0.7"), k("1.0.7"))

    # --- поиск релиза
    def test_find_release(self):
        r = self.m.find_release()
        self.assertEqual(r["version"], "1.0.7"); self.assertTrue(r["name"].endswith("x86_64.AppImage"))

    def test_weird_tag_refused(self):
        Fake.redirect_to = "/botsystemtioxsit/battle-admin-bot/releases/tag/../../evil"
        with self.assertRaises(Exception):
            self.m.find_release()
        Fake.redirect_to = "/botsystemtioxsit/battle-admin-bot/releases/tag/v1.0.7%3Brm%20-rf"
        with self.assertRaises(self.m.LauncherError):
            self.m.find_release()

    def test_no_checksum_refused(self):
        Fake.yml_override = b"version: 1.0.7\nfiles:\n  - url: battle-admin-desktop-1.0.7-x86_64.AppImage\n    size: 5\n"
        with self.assertRaises(self.m.LauncherError) as e:
            self.m.find_release()
        self.assertIn("контрольной суммы", str(e.exception))

    def test_no_appimage_in_release(self):
        Fake.yml_override = b"version: 1.0.7\nfiles:\n  - url: other.deb\n    sha512: AAAA\n"
        with self.assertRaises(self.m.LauncherError):
            self.m.find_release()

    # --- установка
    def test_install_run_remove(self):
        if self.m.platform_machine() not in ("x86_64", "amd64"):
            self.skipTest("не x86_64")
        v = self.m.install_terminal()
        self.assertEqual(v, "1.0.7")
        st = self.m.status(); self.assertTrue(st["installed"]); self.assertEqual(st["version"], "1.0.7")
        f = self.m.installed_file(); self.assertTrue(os.access(f, os.X_OK))
        self.assertEqual(open(f, "rb").read(), Fake.app_bytes)
        d = os.path.join(self.tmp, "apps", "ghost-shell-terminal.desktop")
        self.assertTrue(os.path.isfile(d)); txt = open(d, encoding="utf-8").read()
        self.assertIn("Name=Ghost Shell Terminal", txt); self.assertIn(" run", txt)
        self.assertTrue(self.m.run_terminal())
        self.m.remove_terminal()
        self.assertFalse(self.m.status()["installed"]); self.assertFalse(os.path.exists(d)); self.assertFalse(os.path.exists(f))

    def test_corrupt_download_not_installed(self):
        if self.m.platform_machine() not in ("x86_64", "amd64"):
            self.skipTest("не x86_64")
        Fake.corrupt_app = True
        with self.assertRaises(self.m.LauncherError) as e:
            self.m.install_terminal()
        self.assertIn("не совпала", str(e.exception))
        self.assertFalse(self.m.status()["installed"])
        left = [x for x in os.listdir(os.path.join(self.env["GSL_HOME"], "tmp"))] if os.path.isdir(os.path.join(self.env["GSL_HOME"], "tmp")) else []
        self.assertEqual(left, [], "недокачанный файл должен быть удалён")

    def test_update_replaces_old_file(self):
        if self.m.platform_machine() not in ("x86_64", "amd64"):
            self.skipTest("не x86_64")
        self.m.install_terminal()
        cur, newer, rel = self.m.check_update(); self.assertIsNone(newer)
        Fake.version = "1.0.8"; Fake.app_bytes = b"#!/bin/sh\necho v108\n"
        cur, newer, rel = self.m.check_update(); self.assertEqual((cur, newer), ("1.0.7", "1.0.8"))
        self.assertEqual(self.m.install_terminal(), "1.0.8")
        apps = os.listdir(os.path.join(self.env["GSL_HOME"], "apps", "terminal"))
        self.assertEqual(apps, ["battle-admin-desktop-1.0.8-x86_64.AppImage"], "старая версия должна быть убрана")

    def test_run_without_install(self):
        with self.assertRaises(self.m.LauncherError):
            self.m.run_terminal()

    def test_state_garbage_ignored(self):
        os.makedirs(self.env["GSL_HOME"], exist_ok=True)
        open(os.path.join(self.env["GSL_HOME"], "state.json"), "w").write('{"version":"../../x","file":"../../../etc/passwd"}')
        self.assertFalse(self.m.status()["installed"])

    def test_foreign_host_refused(self):
        with self.assertRaises(self.m.LauncherError):
            self.m.http_get("https://evil.example/x")
        with self.assertRaises(self.m.LauncherError):
            self.m.http_get("http://github.com/x")  # без https (кроме тестового хоста)

    def test_failed_run_reports(self):
        if self.m.platform_machine() not in ("x86_64", "amd64"):
            self.skipTest("не x86_64")
        Fake.app_bytes = b"#!/bin/sh\nexit 3\n"
        self.m.install_terminal()
        with self.assertRaises(self.m.LauncherError) as e:
            self.m.run_terminal()
        self.assertIn("закрылось сразу", str(e.exception))

    # --- сам лаунчер
    def test_self_install_and_update(self):
        self.m.self_install()
        self.assertTrue(os.path.isfile(os.path.join(self.env["GSL_BIN"], "ghost-shell-launcher")))
        self.assertTrue(os.path.isfile(os.path.join(self.tmp, "apps", "ghost-shell-launcher.desktop")))
        self.assertTrue(os.path.isfile(os.path.join(self.env["GSL_HOME"], "launcher", "ghost-shell-launcher.py")))
        # обновление запускается из установленной копии
        inst = os.path.join(self.env["GSL_HOME"], "launcher", "ghost-shell-launcher.py")
        spec = importlib.util.spec_from_file_location("gsl_inst", inst); m2 = importlib.util.module_from_spec(spec); spec.loader.exec_module(m2)
        buf = io.BytesIO()
        with tarfile.open(fileobj=buf, mode="w:gz") as t:
            data = b"VERSION = '9.9.9'\nprint('new')\n"
            ti = tarfile.TarInfo("ghost-shell-launcher.py"); ti.size = len(data); t.addfile(ti, io.BytesIO(data))
        Fake.launcher_tar = buf.getvalue(); Fake.launcher_sha = hashlib.sha256(Fake.launcher_tar).hexdigest()
        self.assertEqual(m2.self_update(), "9.9.9")
        self.assertIn("9.9.9", open(inst).read())
        Fake.launcher_sha = "0" * 64
        with self.assertRaises(m2.LauncherError):
            m2.self_update()

    def test_self_update_not_installed_copy(self):
        with self.assertRaises(self.m.LauncherError):
            self.m.self_update()

    def test_unsafe_tar_refused(self):
        buf = os.path.join(self.tmp, "bad.tgz")
        with tarfile.open(buf, "w:gz") as t:
            ti = tarfile.TarInfo("../evil.py"); ti.size = 1; t.addfile(ti, io.BytesIO(b"x"))
        with self.assertRaises(self.m.LauncherError):
            self.m.safe_extract(buf, os.path.join(self.tmp, "out"))
        with tarfile.open(buf, "w:gz") as t:
            ti = tarfile.TarInfo("link"); ti.type = tarfile.SYMTYPE; ti.linkname = "/etc/passwd"; t.addfile(ti)
        with self.assertRaises(self.m.LauncherError):
            self.m.safe_extract(buf, os.path.join(self.tmp, "out2"))

    def test_launch_command_flags(self):
        cmd, env = self.m.launch_command("/x/app.AppImage")
        self.assertEqual(cmd[0], "/x/app.AppImage")

    def test_cli_status_and_errors(self):
        self.assertEqual(self.m.cli(["status"]), 0)
        self.assertEqual(self.m.cli(["run"]), 1)       # не установлено — понятная ошибка, а не трассировка
        self.assertEqual(self.m.cli(["nope"]), 2)


if __name__ == "__main__":
    unittest.main()
