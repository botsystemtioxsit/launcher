// Проверка модуля «Обои» на настоящей Windows (запускается в CI: GhostStyle.exe --selftest=<папка>).
#include <windows.h>
#include <algorithm>
#include <cstdio>
#include <cstring>
#include <filesystem>
#include "wallpaper_module.hpp"
#include "json.hpp"
#include "win.hpp"

namespace fs = std::filesystem;

namespace wp {

namespace {
unsigned crc32(const std::string& d) {
    unsigned c = 0xFFFFFFFFu;
    for (unsigned char b : d) { c ^= b; for (int k = 0; k < 8; k++) c = (c >> 1) ^ (0xEDB88320u & (0u - (c & 1))); }
    return ~c;
}
void put16(std::string& s, unsigned v) { s += (char)(v & 255); s += (char)((v >> 8) & 255); }
void put32(std::string& s, unsigned v) { put16(s, v & 0xFFFF); put16(s, v >> 16); }
// zip без сжатия: достаточно, чтобы проверить разбор архива (в том числе враждебных имён)
std::string makeZip(const std::vector<std::pair<std::string, std::string>>& files) {
    std::string out, central; unsigned count = 0;
    for (auto& f : files) {
        unsigned off = (unsigned)out.size(), crc = crc32(f.second);
        out += "PK\x03\x04"; put16(out, 20); put16(out, 0); put16(out, 0); put16(out, 0); put16(out, 0x21); put32(out, crc); put32(out, (unsigned)f.second.size()); put32(out, (unsigned)f.second.size());
        put16(out, (unsigned)f.first.size()); put16(out, 0); out += f.first; out += f.second;
        central += "PK\x01\x02"; put16(central, 20); put16(central, 20); put16(central, 0); put16(central, 0); put16(central, 0); put16(central, 0x21); put32(central, crc);
        put32(central, (unsigned)f.second.size()); put32(central, (unsigned)f.second.size()); put16(central, (unsigned)f.first.size()); put16(central, 0); put16(central, 0); put16(central, 0); put16(central, 0); put32(central, 0); put32(central, off); central += f.first;
        count++;
    }
    unsigned cdOff = (unsigned)out.size();
    out += central;
    out += "PK\x05\x06"; put16(out, 0); put16(out, 0); put16(out, count); put16(out, count); put32(out, (unsigned)central.size()); put32(out, cdOff); put16(out, 0);
    return out;
}
void pumpFor(int ms, const std::function<bool()>& done = nullptr) {
    ULONGLONG end = GetTickCount64() + ms;
    while (GetTickCount64() < end) {
        MSG m;
        while (PeekMessageW(&m, nullptr, 0, 0, PM_REMOVE)) { TranslateMessage(&m); DispatchMessageW(&m); }
        if (done && done()) return;
        Sleep(15);
    }
}
}  // namespace

int WallpaperModule::selfTest(const std::wstring& dir, std::string& log) {
    int fails = 0;
    auto check = [&](const std::string& name, bool ok, const std::string& extra = "") { log += (ok ? "PASS " : "FAIL ") + name + (extra.empty() ? "" : " — " + extra) + "\n"; if (!ok) fails++; };
    Settings original = Settings::load();
    std::vector<std::string> made;
    fs::path tmp = fs::path(dir) / L"wp-test"; std::error_code ec; fs::remove_all(tmp, ec); fs::create_directories(tmp, ec);

    ensureBuiltins("selftest");
    auto all = listAll();
    auto has = [&](const std::string& id) { for (auto& w : all) if (w.id == id) return true; return false; };
    for (const char* n : {"b-aurora", "b-digital-rain", "b-neon-clock", "b-neon-grid", "b-starfield", "b-terminal-hud"}) check(std::string("встроенные обои: ") + n, has(n));
    Wallpaper clock, term;
    check("manifest.json читается (neon-clock: 5 настроек)", getWallpaper("b-neon-clock", clock) && clock.params.size() == 5 && clock.params[0].key == "c1" && clock.params[3].max == 30);
    check("«Терминал»: слой, данные sys/input/audio", getWallpaper("b-terminal-hud", term) && term.overlay && std::find(term.telemetry.begin(), term.telemetry.end(), "sys") != term.telemetry.end());
    check("шрифт «Терминала» вшит", fs::exists(fs::path(term.dir) / L"fonts" / L"ShareTechMono-Regular.ttf"));
    check("адрес с настройками: c1 и size", clock.paramQuery().find("c1=%2300F0FF") != std::string::npos && clock.paramQuery().find("size=18") != std::string::npos);
    check("gs.js и compose.html вшиты", readAsset("res/gs.js").size() > 1000 && readAsset("res/compose.html").find("iframe") != std::string::npos);
    std::wstring url = entryUrl(clock, Settings{}, nullptr, L"wallpaper.example", "");
    check("адрес страницы обоев", url.find(L"https://wallpaper.example/index.html?fit=cover&fps=60&muted=1") == 0, gs::narrow(url));

    // свои обои из шаблона
    std::map<std::string, std::string> vals = {{"c1", "#FF0000"}, {"size", "22"}};
    Result cr = createFrom(clock, "Тест часы", vals, "");
    check("создание своих обоев из шаблона", cr.ok, cr.message);
    if (cr.ok) {
        made.push_back(cr.id);
        Wallpaper mine; bool rd = getWallpaper(cr.id, mine);
        check("свои обои читаются, значения сохранены", rd && mine.values["c1"] == "#FF0000" && mine.values["size"] == "22" && !mine.builtin() && fs::exists(fs::path(mine.dir) / L"index.html"));
        vals["size"] = "25"; updateValues(mine, vals, ""); Wallpaper again; getWallpaper(cr.id, again);
        check("правка своих обоев", again.values["size"] == "25");
        Wallpaper b; getWallpaper("b-neon-clock", b); updateValues(b, vals, "");
        Wallpaper b2; getWallpaper("b-neon-clock", b2);
        check("встроенные обои не меняются", b2.values.find("size") == b2.values.end() || b2.values["size"] != "25");
    }

    // ввоз файлов
    auto write = [&](const char* name, const std::string& data) { fs::path p = tmp / name; gs::writeFileAtomic(p.wstring(), data); return p.wstring(); };
    auto imp = [&](const char* name, const std::string& data, const char* label, const char* type) {
        Result r = installPath(write(name, data), "проверка");
        if (r.ok) made.push_back(r.id);
        Wallpaper w;
        check(std::string("ввоз: ") + label, r.ok && getWallpaper(r.id, w) && w.type == type, r.message);
        return w;
    };
    Wallpaper h = imp("page.html", "<!doctype html><title>x</title>", "страница html", "html");
    check("страница: главный файл", h.entry == "page.html");
    Wallpaper v = imp("clip.mp4", std::string("\0\0\0\x18" "ftypmp42", 12) + "data", "видео mp4", "video");
    check("видео: проигрыватель и имя файла", v.entry == "_player.html" && mediaFile(v) == "clip.mp4" && fs::exists(fs::path(v.dir) / L"clip.mp4"));
    imp("anim.gif", "GIF89a\x01\x00\x01\x00", "GIF", "gif");
    imp("pic.png", "\x89PNG\r\n\x1a\n", "картинка png", "image");
    for (const char* bad : {"evil.exe", "x.7z", "y.bat", "z.dll"}) {
        Result r = installPath(write(bad, "MZ"), "проверка");
        check(std::string("отказ: ") + bad, !r.ok, r.message);
        if (r.ok) made.push_back(r.id);
    }
    // папка с index.html: лишнее не копируется
    fs::create_directories(tmp / L"folder", ec);
    gs::writeFileAtomic((tmp / L"folder" / L"index.html").wstring(), "<html>x</html>"); gs::writeFileAtomic((tmp / L"folder" / L"run.exe").wstring(), "MZ"); gs::writeFileAtomic((tmp / L"folder" / L"app.js").wstring(), "1");
    Result fr = installPath((tmp / L"folder").wstring(), "проверка");
    check("ввоз папки", fr.ok, fr.message);
    if (fr.ok) { made.push_back(fr.id); Wallpaper w; getWallpaper(fr.id, w); check("в папке отброшен .exe, взят .js", !fs::exists(fs::path(w.dir) / L"run.exe") && fs::exists(fs::path(w.dir) / L"app.js")); }
    // zip
    std::string lively = "{\"Title\":\"Мой Lively\",\"Desc\":\"тест\",\"Type\":0,\"FileName\":\"index.html\"}";
    Result zr = installPath(write("good.zip", makeZip({{"pack/index.html", "<html>z</html>"}, {"pack/LivelyInfo.json", lively}, {"pack/run.exe", "MZ"}, {"pack/s.js", "1"}})), "проверка");
    check("ввоз zip (Lively)", zr.ok, zr.message);
    if (zr.ok) { made.push_back(zr.id); Wallpaper w; getWallpaper(zr.id, w); check("zip: название из LivelyInfo, .exe отброшен", w.title == "Мой Lively" && !fs::exists(fs::path(w.dir) / L"run.exe") && fs::exists(fs::path(w.dir) / L"s.js")); }
    Result er = installPath(write("evil.zip", makeZip({{"index.html", "<html></html>"}, {"../../evil-escape.txt", "boo"}})), "проверка");
    check("zip с выходом из папки отклонён", !er.ok, er.message);
    check("ничего не записано за пределы", !fs::exists(fs::path(dir).parent_path() / L"evil-escape.txt") && !fs::exists(fs::path(libraryDir()).parent_path() / L"evil-escape.txt"));
    Result br = installPath(write("broken.zip", "this is not a zip"), "проверка");
    check("битый zip отклонён", !br.ok, br.message);

    // настройки
    Settings s = original; s.Fit = "contain"; s.Fps = 30; s.WeatherName = "Тест"; s.save();
    Settings s2 = Settings::load();
    check("настройки: запись и чтение", s2.Fit == "contain" && s2.Fps == 30 && s2.WeatherName == "Тест");

    // настоящий показ
    if (!sp::WebView::available()) log += "ПРОПУСК показа: нет WebView2\n";
    else {
        Settings live = original; live.Active = "b-terminal-hud"; live.Paused = false; live.PauseOnFullscreen = false; live.PauseWhenCovered = false; live.AllMonitors = false; live.Mute = true; live.TelAudio = true; live.TelJournal = true;
        live.WeatherName.clear();
        ctl_.apply(live);
        pumpFor(60000, [&] { return ctl_.allReady() || ctl_.anyFailed(); });
        log += ctl_.describe();
        check("«Терминал» открылся на рабочем столе", ctl_.allReady(), gs::narrow(ctl_.lastErrors()));
        std::string desc = ctl_.describe();
        bool attached = ctl_.hostCount() > 0 && (desc.find("родитель Progman") != std::string::npos || desc.find("родитель WorkerW") != std::string::npos);
        check("окно обоев — дочернее окно рабочего стола", attached);
        pumpFor(5000);
        std::string res; bool got = false;
        ctl_.executeOnFirst(L"JSON.stringify({gs: !!window.GS, sys: !!(window.GS&&GS.data&&GS.data.sys), inp: !!(window.GS&&GS.data&&GS.data.input), host: location.host})", [&](const std::string& r) { res = r; got = true; });
        pumpFor(8000, [&] { return got; });
        log += "страница: " + res + "\n";
        check("страница получила данные компьютера (sys, input)", res.find("sys\\\":true") != std::string::npos && res.find("inp\\\":true") != std::string::npos, res);
        check("адрес страницы — виртуальный хост", res.find("wallpaper.example") != std::string::npos);
        // выход в интернет закрыт
        got = false; res.clear();
        ctl_.executeOnFirst(L"window.__r='';(function(){var i=new Image();i.onload=function(){window.__r='open'};i.onerror=function(){window.__r='blocked'};i.src='https://www.google.com/favicon.ico?'+Date.now();})();0", [&](const std::string&) { got = true; });
        pumpFor(8000, [&] { return got; });
        pumpFor(6000);
        got = false;
        ctl_.executeOnFirst(L"window.__r", [&](const std::string& r) { res = r; got = true; });
        pumpFor(8000, [&] { return got; });
        check("страница не выходит в интернет", res == "\"blocked\"", res);
        // смена обоев в том же окне
        live.Active = "b-aurora"; ctl_.apply(live);
        pumpFor(4000);
        got = false; res.clear();
        ctl_.executeOnFirst(L"location.pathname + ' ' + location.search", [&](const std::string& r) { res = r; got = true; });
        pumpFor(8000, [&] { return got; });
        check("смена обоев без пересоздания окна", ctl_.hostCount() == 1 && res.find("/index.html") != std::string::npos && res.find("fit=cover") != std::string::npos, res);
        // свои обои с настройками и видео-проигрыватель
        if (cr.ok) { live.Active = cr.id; ctl_.apply(live); pumpFor(4000); got = false; res.clear(); ctl_.executeOnFirst(L"location.search", [&](const std::string& r) { res = r; got = true; }); pumpFor(8000, [&] { return got; });
            check("свои обои: значения в адресе", res.find("c1=%23FF0000") != std::string::npos && res.find("size=25") != std::string::npos, res); }
        live.Active = v.id; ctl_.apply(live); pumpFor(4000); got = false; res.clear();
        ctl_.executeOnFirst(L"document.title + ' ' + (document.querySelector('video')? 'video' : 'нет видео')", [&](const std::string& r) { res = r; got = true; }); pumpFor(8000, [&] { return got; });
        check("видео-проигрыватель создан", res.find("video") != std::string::npos, res);
        // пауза
        live.Paused = true; ctl_.apply(live);
        pumpFor(3500, [&] { return ctl_.pausedAll(); });
        check("пауза обоев", ctl_.pausedAll());
        // выключение
        live.Active.clear(); ctl_.apply(live);
        check("выключение: окон не осталось", ctl_.hostCount() == 0);
    }

    for (auto& id : made) removeWallpaper(id);
    original.save();
    fs::remove_all(tmp, ec);
    return fails;
}

}  // namespace wp
