#!/usr/bin/env python3
"""
AI Plus: маленький веб-сервер на вашем сервере. Отдаёт страницу чата (chat.html) и пропускает к Ollama только нужные запросы.
Слушает ТОЛЬКО адреса внутри VPN-туннеля (WireGuard / GSP) — из интернета он не виден. Только стандартная библиотека Python.
Запуск: python3 gateway.py --bind 10.66.66.1,10.78.0.1 --port 8080 --page /opt/ai-plus/chat.html
"""
import argparse
import http.client
import json
import os
import socket
import sys
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

OLLAMA = ("127.0.0.1", 11434)
ALLOWED_POST = {"/api/chat"}
ALLOWED_GET = {"/api/tags", "/api/version"}
MAX_BODY = 4 * 1024 * 1024
PAGE = b""


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.0"   # без Content-Length: ответ нейросети идёт потоком, соединение закрывается в конце
    server_version = "AIPlus"

    def log_message(self, fmt, *args):   # в журнал — без текста запросов
        sys.stderr.write("%s %s\n" % (self.address_string(), fmt % args if args else fmt))

    def _send(self, code, body, ctype="text/plain; charset=utf-8"):
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        path = self.path.split("?", 1)[0]
        if path in ("/", "/index.html", "/chat.html"):
            return self._send(200, PAGE, "text/html; charset=utf-8")
        if path in ALLOWED_GET:
            return self._proxy("GET", path, None)
        if path == "/health":
            return self._send(200, b"ok")
        self._send(404, "не найдено".encode())

    def do_POST(self):
        path = self.path.split("?", 1)[0]
        if path not in ALLOWED_POST:
            return self._send(403, "нельзя".encode())
        try:
            n = int(self.headers.get("Content-Length", "0"))
        except ValueError:
            return self._send(400, b"bad length")
        if n <= 0 or n > MAX_BODY:
            return self._send(413, b"too large")
        body = self.rfile.read(n)
        try:
            json.loads(body)   # мусор в Ollama не пускаем
        except Exception:
            return self._send(400, b"bad json")
        self._proxy("POST", path, body)

    def _proxy(self, method, path, body):
        try:
            c = http.client.HTTPConnection(*OLLAMA, timeout=600)
            headers = {"Content-Type": "application/json"} if body is not None else {}
            c.request(method, path, body=body, headers=headers)
            r = c.getresponse()
        except Exception as e:
            return self._send(502, ("нейросеть (Ollama) не отвечает: %s" % e).encode())
        self.send_response(r.status)
        self.send_header("Content-Type", r.getheader("Content-Type", "application/x-ndjson"))
        self.send_header("Cache-Control", "no-store")
        self.send_header("X-Accel-Buffering", "no")
        self.end_headers()
        try:
            while True:
                chunk = r.read1(4096) if hasattr(r, "read1") else r.read(4096)
                if not chunk:
                    break
                self.wfile.write(chunk)
                self.wfile.flush()
        except (BrokenPipeError, ConnectionResetError):
            pass   # закрыли вкладку или нажали «Стоп»
        finally:
            c.close()


class V4Server(ThreadingHTTPServer):
    daemon_threads = True
    allow_reuse_address = True


def main():
    global PAGE
    ap = argparse.ArgumentParser()
    ap.add_argument("--bind", required=True, help="адреса через запятую (внутри VPN-туннеля)")
    ap.add_argument("--port", type=int, default=8080)
    ap.add_argument("--page", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "chat.html"))
    ap.add_argument("--ollama", default="127.0.0.1:11434")
    a = ap.parse_args()
    h, p = a.ollama.rsplit(":", 1)
    global OLLAMA
    OLLAMA = (h, int(p))
    with open(a.page, "rb") as f:
        PAGE = f.read()
    servers = []
    for addr in [x.strip() for x in a.bind.split(",") if x.strip()]:
        if addr in ("0.0.0.0", "::", ""):
            sys.exit("Отказ: слушать все адреса нельзя — только адрес внутри VPN-туннеля.")
        try:
            s = V4Server((addr, a.port), Handler)
        except OSError as e:
            sys.stderr.write("не удалось занять %s:%d (%s)\n" % (addr, a.port, e))
            continue
        servers.append(s)
        sys.stderr.write("слушаю http://%s:%d\n" % (addr, a.port))
    if not servers:
        sys.exit("Нет ни одного адреса для запуска.")
    for s in servers[1:]:
        threading.Thread(target=s.serve_forever, daemon=True).start()
    servers[0].serve_forever()


if __name__ == "__main__":
    main()
