# Снимки окна лаунчера на виртуальном экране (xvfb) с «поддельным GitHub». Запуск: xvfb-run -a /usr/bin/python3 tests/linux/gui_shot.py <папка>
import importlib.util, os, sys, tempfile, threading, time
here = os.path.dirname(os.path.abspath(__file__))
out = sys.argv[1] if len(sys.argv) > 1 else tempfile.mkdtemp()
os.makedirs(out, exist_ok=True)
spec = importlib.util.spec_from_file_location("tl", os.path.join(here, "test_launcher.py")); tl = importlib.util.module_from_spec(spec); sys.modules["tl"] = tl; spec.loader.exec_module(tl)
srv = tl.Srv(("127.0.0.1", 0), tl.H); threading.Thread(target=srv.serve_forever, daemon=True).start()
tmp = tempfile.mkdtemp()
os.environ.update({"GSL_TEST_BASE": "http://127.0.0.1:%d" % srv.server_address[1], "GSL_HOME": tmp + "/home", "GSL_BIN": tmp + "/bin", "GSL_APPLICATIONS": tmp + "/apps", "GSL_ICONS": tmp + "/icons"})
spec = importlib.util.spec_from_file_location("gsl", os.path.join(here, "..", "..", "Linux", "ghost-shell-launcher.py")); m = importlib.util.module_from_spec(spec); spec.loader.exec_module(m)
tl.Fake.app_bytes = b"#!/bin/sh\nsleep 5\n"

def ready(w, Gtk, GLib, Gdk):
    def shot(name):
        win = w.get_window()
        pb = Gdk.pixbuf_get_from_window(win, 0, 0, win.get_width(), win.get_height())
        pb.savev(os.path.join(out, name), "png", [], [])
        print("снимок", name, flush=True)
        return False
    seq = [(2500, lambda: shot("1-not-installed.png")),
           (2600, lambda: (w.on_main(), False)[1]),
           (6500, lambda: shot("2-installed.png")),
           (6600, lambda: (setattr(tl.Fake, "version", "1.0.8"), setattr(tl.Fake, "app_bytes", b"#!/bin/sh\nsleep 5\n#v8\n"), w.refresh_state(check_net=True), False)[3]),
           (9000, lambda: shot("3-update.png")),
           (9100, lambda: (w.on_run(), False)[1]),
           (11500, lambda: shot("4-run.png")),
           (11600, lambda: (Gtk.main_quit(), False)[1])]
    for t, f in seq:
        GLib.timeout_add(t, f)

sys.exit(m.run_gui(ready))
