"""Проверки cleanup.py на подставной базе и подставном сервере токенов (без сети и без настоящих ключей)."""
import base64
import contextlib
import io
import json
import os
import subprocess
import sys
import tempfile
import threading
import unittest
from http.server import BaseHTTPRequestHandler, HTTPServer
from urllib.parse import parse_qs, urlparse

sys.path.insert(0, os.path.dirname(__file__))
import cleanup  # noqa: E402

NOW = 1_800_000_000_000  # любое «сейчас»
DAY = cleanup.DAY_MS


def aid(n):
    return f"{n:024x}"


class Fake:
    """Маленькая подставная Realtime Database: GET узла (+shallow) и PATCH корня с путями через слэш."""

    def __init__(self, tree):
        self.tree = tree
        self.requests = []
        outer = self

        class H(BaseHTTPRequestHandler):
            def log_message(self, *a):
                pass

            def _send(self, code, obj):
                b = json.dumps(obj).encode()
                self.send_response(code)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(b)))
                self.end_headers()
                self.wfile.write(b)

            def do_GET(self):
                u = urlparse(self.path)
                path = u.path[1:].removesuffix(".json")
                outer.requests.append(("GET", path, self.headers.get("Authorization")))
                node = outer.tree.get(path)
                if parse_qs(u.query).get("shallow") and isinstance(node, dict):
                    node = {k: True for k in node}
                self._send(200, node)

            def do_PATCH(self):
                u = urlparse(self.path)
                body = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
                outer.requests.append(("PATCH", u.path, len(body)))
                for p, v in body.items():
                    top, key = p.split("/", 1)
                    if v is None:
                        outer.tree.get(top, {}).pop(key, None)
                    else:
                        outer.tree.setdefault(top, {})[key] = v
                self._send(200, body)

        self.srv = HTTPServer(("127.0.0.1", 0), H)
        self.url = f"http://127.0.0.1:{self.srv.server_port}"
        threading.Thread(target=self.srv.serve_forever, daemon=True).start()

    def close(self):
        self.srv.shutdown()


def run(argv):
    out = io.StringIO()
    with contextlib.redirect_stdout(out):
        code = cleanup.main(argv)
    return code, out.getvalue()


class PlanTests(unittest.TestCase):
    def test_rules(self):
        accounts = {
            aid(1): {"name": "old", "createdAt": NOW - 400 * DAY, "lastSeen": NOW - 300 * DAY},
            aid(2): {"name": "fresh", "createdAt": NOW - 400 * DAY, "lastSeen": NOW - 10 * DAY},
            aid(3): {"name": "never-seen-old", "createdAt": NOW - 200 * DAY},
            aid(4): {"name": "never-seen-new", "createdAt": NOW - 20 * DAY},
            aid(5): {"name": "blocked-old", "createdAt": NOW - 400 * DAY, "lastSeen": NOW - 400 * DAY},
            aid(6): {"name": "no-dates"},
            aid(7): "строка вместо записи",
            "GARBAGE": {"name": "x", "createdAt": 1},
            aid(8): {"name": "edge-182", "createdAt": NOW - 182 * DAY, "lastSeen": NOW - 182 * DAY},
            aid(9): {"name": "edge-184", "createdAt": NOW - 184 * DAY, "lastSeen": NOW - 184 * DAY},
            aid(10): {"name": "bool-date", "createdAt": True, "lastSeen": "вчера"},
            aid(11): {"name": "future", "createdAt": NOW + 5 * DAY, "lastSeen": NOW + 5 * DAY},
            aid(12): {"name": "last-new-created-old", "createdAt": NOW - 400 * DAY, "lastSeen": NOW - 1 * DAY},
        }
        stale, stats = cleanup.plan(accounts, {aid(5)}, NOW, 183)
        self.assertEqual(sorted(stale), sorted([aid(1), aid(3), aid(9)]))
        self.assertEqual(stats["заблокированные (оставлены)"], 1)
        self.assertEqual(stats["без дат (оставлены)"], 2)  # aid(6) и aid(10) (даты не числа)
        self.assertEqual(stats["повреждённые (оставлены)"], 2)  # aid(7) и GARBAGE


class RunTests(unittest.TestCase):
    def setUp(self):
        self.tree = {
            "gatewayAccounts": {
                aid(1): {"name": "A", "createdAt": NOW - 400 * DAY, "lastSeen": NOW - 300 * DAY},
                aid(2): {"name": "B", "createdAt": NOW - 400 * DAY, "lastSeen": NOW - 5 * DAY},
                aid(3): {"name": "C", "createdAt": NOW - 500 * DAY, "lastSeen": NOW - 500 * DAY},
            },
            "gatewayBlocks": {aid(3): {"at": 1, "reason": "x", "by": "me"}},
            "gatewayBlockedDevices": {"0" * 32: {"at": 1}},
        }
        self.fake = Fake(self.tree)
        self.addCleanup(self.fake.close)

    def argv(self, *extra):
        return ["--db-url", self.fake.url, "--token", "tok", "--now-ms", str(NOW), *extra]

    def test_dry_run_changes_nothing(self):
        before = json.dumps(self.tree, sort_keys=True)
        code, out = run(self.argv("--dry-run"))
        self.assertEqual(code, 0)
        self.assertEqual(json.dumps(self.tree, sort_keys=True), before)
        self.assertIn("к удалению: 1", out)

    def test_real_run_deletes_only_stale_unblocked(self):
        code, out = run(self.argv())
        self.assertEqual(code, 0, out)
        self.assertEqual(sorted(self.tree["gatewayAccounts"]), sorted([aid(2), aid(3)]))  # 1 удалён, 2 активен, 3 заблокирован
        self.assertIn(aid(3), self.tree["gatewayBlocks"])
        self.assertIn("0" * 32, self.tree["gatewayBlockedDevices"])
        self.assertIn("удалено: 1", out)

    def test_log_has_no_ids_or_names(self):
        _, out = run(self.argv())
        for s in (aid(1), aid(2), aid(3), "A", "B", "C", "tok"):
            if len(s) > 1:
                self.assertNotIn(s, out)

    def test_token_sent_as_header_not_url(self):
        run(self.argv("--dry-run"))
        gets = [r for r in self.fake.requests if r[0] == "GET"]
        self.assertTrue(gets and all(r[2] == "Bearer tok" for r in gets))

    def test_guard_stops_mass_delete(self):
        accs = {aid(i): {"name": "x", "createdAt": NOW - 900 * DAY, "lastSeen": NOW - 900 * DAY} for i in range(1, 41)}
        accs[aid(99)] = {"name": "live", "createdAt": NOW - 900 * DAY, "lastSeen": NOW - DAY}
        self.tree["gatewayAccounts"] = accs
        self.tree["gatewayBlocks"] = {}
        code, out = run(self.argv())
        self.assertEqual(code, 2)
        self.assertEqual(len(self.tree["gatewayAccounts"]), 41)
        code, out = run(self.argv("--force"))
        self.assertEqual(code, 0)
        self.assertEqual(list(self.tree["gatewayAccounts"]), [aid(99)])
        patches = [r for r in self.fake.requests if r[0] == "PATCH"]
        self.assertEqual([p[2] for p in patches], [40])  # одним пакетом (≤100)

    def test_batches_of_100(self):
        self.tree["gatewayAccounts"] = {aid(i): {"name": "x", "createdAt": NOW - 900 * DAY, "lastSeen": NOW - 900 * DAY} for i in range(1, 251)}
        self.tree["gatewayBlocks"] = {}
        code, _ = run(self.argv("--force"))
        self.assertEqual(code, 0)
        self.assertEqual([p[2] for p in self.fake.requests if p[0] == "PATCH"], [100, 100, 50])
        self.assertEqual(self.tree["gatewayAccounts"], {})

    def test_empty_database(self):
        self.tree.pop("gatewayAccounts")
        code, out = run(self.argv())
        self.assertEqual(code, 0)
        self.assertIn("удалять нечего", out)

    def test_refuses_foreign_database_and_short_period(self):
        with self.assertRaises(SystemExit):
            cleanup.main(["--db-url", "https://zolotaya-kletka-default-rtdb.firebaseio.com", "--token", "t"])
        with self.assertRaises(SystemExit):
            cleanup.main(["--db-url", self.fake.url, "--token", "t", "--days", "30"])

    def test_collection_shape_is_checked(self):
        self.tree["gatewayAccounts"] = ["не", "словарь"]
        with self.assertRaises(SystemExit):
            run(self.argv())


class TokenTests(unittest.TestCase):
    def test_jwt_is_signed_and_exchanged(self):
        d = tempfile.mkdtemp()
        key, pub = os.path.join(d, "k.pem"), os.path.join(d, "p.pem")
        subprocess.run(["openssl", "genrsa", "-out", key, "2048"], check=True, capture_output=True)
        subprocess.run(["openssl", "rsa", "-in", key, "-pubout", "-out", pub], check=True, capture_output=True)
        got = {}

        class H(BaseHTTPRequestHandler):
            def log_message(self, *a):
                pass

            def do_POST(self):
                form = parse_qs(self.rfile.read(int(self.headers["Content-Length"])).decode())
                got["form"] = form
                b = json.dumps({"access_token": "fake-access"}).encode()
                self.send_response(200)
                self.send_header("Content-Length", str(len(b)))
                self.end_headers()
                self.wfile.write(b)

        srv = HTTPServer(("127.0.0.1", 0), H)
        threading.Thread(target=srv.serve_forever, daemon=True).start()
        self.addCleanup(srv.shutdown)
        sa = {"client_email": "svc@vpn0dns.iam.gserviceaccount.com", "private_key": open(key).read(),
              "token_uri": f"http://127.0.0.1:{srv.server_port}/token"}
        self.assertEqual(cleanup.mint_token(sa, now=1000), "fake-access")
        self.assertEqual(got["form"]["grant_type"], ["urn:ietf:params:oauth:grant-type:jwt-bearer"])
        head, claim, sig = got["form"]["assertion"][0].split(".")
        pad = lambda s: s + "=" * (-len(s) % 4)  # noqa: E731
        c = json.loads(base64.urlsafe_b64decode(pad(claim)))
        self.assertEqual(c["iss"], sa["client_email"])
        self.assertEqual((c["iat"], c["exp"]), (1000, 4000))
        self.assertIn("firebase.database", c["scope"])
        sigf = os.path.join(d, "s.bin")
        open(sigf, "wb").write(base64.urlsafe_b64decode(pad(sig)))
        r = subprocess.run(["openssl", "dgst", "-sha256", "-verify", pub, "-signature", sigf], input=f"{head}.{claim}".encode(), capture_output=True)
        self.assertEqual(r.returncode, 0, r.stderr)
        # временный файл с ключом убран
        self.assertEqual([f for f in os.listdir(tempfile.gettempdir()) if f.startswith("gw-key-")], [])


if __name__ == "__main__":
    unittest.main()
