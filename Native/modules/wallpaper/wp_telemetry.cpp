#include "wp_telemetry.hpp"
#include <windows.h>
#include <audioclient.h>
#include <dwmapi.h>
#include <endpointvolume.h>
#include <winsock2.h>
#include <ws2ipdef.h>
#include <iphlpapi.h>
#include <mmdeviceapi.h>
#include <netioapi.h>
#include <psapi.h>
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <ctime>
#include "json.hpp"
#include "win.hpp"

namespace wp {

using gs::Json;

static std::string num(double v, int dec = 1) { char b[40]; std::snprintf(b, sizeof b, "%.*f", dec, v); return b; }
static std::string msg(const std::string& kind, const Json& data) {
    Json m = Json::Object(); m.set("gs", Json::Number(1)); m.set("k", Json::String(kind)); m.set("d", data);
    std::string out; gs::jsonDump(m, out); return out;
}

// ---------- звук ----------
static void fft(std::vector<double>& re, std::vector<double>& im) {
    size_t n = re.size();
    for (size_t i = 1, j = 0; i < n; i++) {
        size_t bit = n >> 1;
        for (; j & bit; bit >>= 1) j ^= bit;
        j ^= bit;
        if (i < j) { std::swap(re[i], re[j]); std::swap(im[i], im[j]); }
    }
    for (size_t len = 2; len <= n; len <<= 1) {
        double ang = -2 * 3.14159265358979323846 / (double)len, wr = std::cos(ang), wi = std::sin(ang);
        for (size_t i = 0; i < n; i += len) {
            double cr = 1, ci = 0;
            for (size_t k = 0; k < len / 2; k++) {
                double ur = re[i + k], ui = im[i + k];
                double vr = re[i + k + len / 2] * cr - im[i + k + len / 2] * ci, vi = re[i + k + len / 2] * ci + im[i + k + len / 2] * cr;
                re[i + k] = ur + vr; im[i + k] = ui + vi; re[i + k + len / 2] = ur - vr; im[i + k + len / 2] = ui - vi;
                double t = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = t;
            }
        }
    }
}

void AudioTap::start() { if (run_) return; run_ = true; th_ = std::thread([this] { loop(); }); }
void AudioTap::stop() { run_ = false; if (th_.joinable()) th_.join(); active_ = false; }

void AudioTap::loop() {
    CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    IMMDeviceEnumerator* en = nullptr;
    CoCreateInstance(__uuidof(MMDeviceEnumerator), nullptr, CLSCTX_ALL, __uuidof(IMMDeviceEnumerator), (void**)&en);
    while (run_ && en) {
        IMMDevice* dev = nullptr; IAudioClient* ac = nullptr; IAudioCaptureClient* cc = nullptr; IAudioEndpointVolume* ev = nullptr;
        WAVEFORMATEX* fmt = nullptr; std::wstring id;
        bool ok = SUCCEEDED(en->GetDefaultAudioEndpoint(eRender, eMultimedia, &dev)) && dev;
        if (ok) { LPWSTR w = nullptr; if (SUCCEEDED(dev->GetId(&w)) && w) { id = w; CoTaskMemFree(w); } }
        ok = ok && SUCCEEDED(dev->Activate(__uuidof(IAudioClient), CLSCTX_ALL, nullptr, (void**)&ac)) && ac && SUCCEEDED(ac->GetMixFormat(&fmt)) && fmt;
        ok = ok && SUCCEEDED(ac->Initialize(AUDCLNT_SHAREMODE_SHARED, AUDCLNT_STREAMFLAGS_LOOPBACK, 10000000, 0, fmt, nullptr));
        ok = ok && SUCCEEDED(ac->GetService(__uuidof(IAudioCaptureClient), (void**)&cc)) && cc;
        if (dev) dev->Activate(__uuidof(IAudioEndpointVolume), CLSCTX_ALL, nullptr, (void**)&ev);
        if (ok && SUCCEEDED(ac->Start())) {
            rate_ = (int)fmt->nSamplesPerSec; active_ = true;
            int ch = std::max<int>(1, fmt->nChannels), bits = fmt->wBitsPerSample;
            bool isFloat = fmt->wFormatTag == WAVE_FORMAT_IEEE_FLOAT || (fmt->wFormatTag == WAVE_FORMAT_EXTENSIBLE && bits == 32);
            ULONGLONG nextVol = 0, nextDev = GetTickCount64() + 5000;
            while (run_) {
                Sleep(10);
                UINT32 n = 0;
                while (SUCCEEDED(cc->GetNextPacketSize(&n)) && n > 0) {
                    BYTE* data = nullptr; UINT32 frames = 0; DWORD flags = 0;
                    if (FAILED(cc->GetBuffer(&data, &frames, &flags, nullptr, nullptr))) break;
                    {
                        std::lock_guard<std::mutex> g(mu_);
                        lastData_ = GetTickCount64();
                        bool silent = flags & AUDCLNT_BUFFERFLAGS_SILENT;
                        for (UINT32 i = 0; i < frames; i++) {
                            float sum = 0;
                            if (!silent) {
                                for (int c = 0; c < ch; c++) {
                                    if (isFloat && bits == 32) sum += ((const float*)data)[i * ch + c];
                                    else if (bits == 16) sum += ((const short*)data)[i * ch + c] / 32768.f;
                                }
                                sum /= ch;
                            }
                            ring_[wpos_] = sum; wpos_ = (wpos_ + 1) % kFft;
                        }
                    }
                    cc->ReleaseBuffer(frames);
                }
                ULONGLONG now = GetTickCount64();
                if (ev && now >= nextVol) { nextVol = now + 250; float v = 0; BOOL m = FALSE; if (SUCCEEDED(ev->GetMasterVolumeLevelScalar(&v))) vol_ = v; if (SUCCEEDED(ev->GetMute(&m))) muted_ = m != FALSE; }
                if (now >= nextDev) {
                    nextDev = now + 5000;
                    IMMDevice* d2 = nullptr; LPWSTR w = nullptr; bool changed = false;
                    if (SUCCEEDED(en->GetDefaultAudioEndpoint(eRender, eMultimedia, &d2)) && d2) { if (SUCCEEDED(d2->GetId(&w)) && w) { changed = id != w; CoTaskMemFree(w); } d2->Release(); }
                    if (changed) break;
                }
            }
            ac->Stop();
        } else { for (int i = 0; i < 50 && run_; i++) Sleep(100); }   // нет устройства — пробуем снова через 5 с
        active_ = false;
        if (fmt) CoTaskMemFree(fmt);
        if (ev) ev->Release(); if (cc) cc->Release(); if (ac) ac->Release(); if (dev) dev->Release();
    }
    if (en) en->Release();
    CoUninitialize();
}

std::string AudioTap::snapshot() {
    float frame[kFft]; bool fresh;
    {
        std::lock_guard<std::mutex> g(mu_);
        fresh = GetTickCount64() - lastData_ < 150;
        for (int i = 0; i < kFft; i++) frame[i] = ring_[(wpos_ + i) % kFft];
    }
    if (!fresh) { for (auto& b : bands_) b *= 0.82f; level_ *= 0.82f; }
    else {
        double rms = 0; for (float f : frame) rms += (double)f * f;
        rms = std::sqrt(rms / kFft);
        level_ = std::max((float)std::min(1.0, rms * 4), level_ * 0.85f);
        std::vector<double> re(kFft), im(kFft, 0.0);
        for (int i = 0; i < kFft; i++) re[i] = frame[i] * (0.5 - 0.5 * std::cos(2 * 3.14159265358979323846 * i / (kFft - 1)));
        fft(re, im);
        double binHz = (double)rate_ / kFft, maxHz = std::min(16000.0, rate_ / 2.0);
        for (int b = 0; b < kBands; b++) {
            double f0 = 40 * std::pow(maxHz / 40, (double)b / kBands), f1 = 40 * std::pow(maxHz / 40, (double)(b + 1) / kBands);
            int i0 = std::max(1, (int)std::floor(f0 / binHz)), i1 = std::max(i0 + 1, (int)std::ceil(f1 / binHz));
            double m = 0;
            for (int i = i0; i < std::min(i1, kFft / 2); i++) m = std::max(m, std::sqrt(re[i] * re[i] + im[i] * im[i]));
            double db = 20 * std::log10(m / (kFft / 4.0) + 1e-9);
            float v = (float)std::clamp((db + 62) / 52, 0.0, 1.0);
            bands_[b] = std::max(v, bands_[b] * 0.8f);
        }
    }
    Json d = Json::Object();
    d.set("level", Json::Number(std::round(level_ * 1000) / 1000));
    Json b = Json::Array(); for (float x : bands_) b.a.push_back(Json::Number(std::round(x * 1000) / 1000));
    d.set("bands", b); d.set("vol", Json::Number(std::round(vol_ * 1000) / 1000)); d.set("muted", Json::Boolean(muted_)); d.set("active", Json::Boolean(active_));
    return msg("audio", d);
}

// ---------- такт ----------
Telemetry::~Telemetry() { stopAll(); for (auto& t : workers_) if (t.joinable()) t.join(); }

void Telemetry::setConfig(const Settings& s) {
    bool weatherChanged = s.WeatherName != cfg_.WeatherName || s.WeatherLat != cfg_.WeatherLat || s.WeatherLon != cfg_.WeatherLon;
    cfg_ = s;
    if (weatherChanged) nWeather_ = 0;
}
void Telemetry::setWanted(const std::set<std::string>& kinds) {
    wanted_ = kinds;
    bool needAudio = cfg_.TelAudio && wanted_.count("audio");
    if (needAudio) audio_.start(); else if (audio_.running()) audio_.stop();
    if (wanted_.empty() && started_) { started_ = false; }
    if (!wanted_.empty() && !started_) { started_ = true; addJournal("INFO", "TERM", "terminal online"); nWeather_ = 0; }
}
void Telemetry::stopAll() { audio_.stop(); wanted_.clear(); started_ = false; winsPrimed_ = false; lastWins_.clear(); lastFocus_.clear(); }

void Telemetry::addJournal(const std::string& lv, const std::string& src, std::string m) {
    if (m.size() > 60) { size_t c = 60; while (c > 0 && ((unsigned char)m[c] & 0xC0) == 0x80) c--; m.resize(c); }
    journal_.push_back({++journalId_, (long long)(long long)std::time(nullptr) * 1000LL, lv, src, m});
    if (journal_.size() > 200) journal_.erase(journal_.begin(), journal_.begin() + (journal_.size() - 200));
}

static ULONGLONG U(FILETIME f) { return ((ULONGLONG)f.dwHighDateTime << 32) | f.dwLowDateTime; }

std::string Telemetry::sysJson(ULONGLONG now) {
    FILETIME idle, kernel, user;
    if (GetSystemTimes(&idle, &kernel, &user)) {
        ULONGLONG i = U(idle), k = U(kernel), u = U(user);
        if (lastKernel_) { ULONGLONG dI = i - lastIdle_, dT = (k - lastKernel_) + (u - lastUser_); if (dT > 0) cpu_ = std::clamp((1.0 - (double)dI / (double)dT) * 100, 0.0, 100.0); }
        lastIdle_ = i; lastKernel_ = k; lastUser_ = u;
    }
    MEMORYSTATUSEX ms{}; ms.dwLength = sizeof ms; double usedMb = 0, totalMb = 0;
    if (GlobalMemoryStatusEx(&ms)) { ram_ = ms.dwMemoryLoad; totalMb = ms.ullTotalPhys / 1048576.0; usedMb = totalMb - ms.ullAvailPhys / 1048576.0; }
    ULONGLONG down = 0, up = 0;
    PMIB_IF_TABLE2 tbl = nullptr;
    if (GetIfTable2(&tbl) == NO_ERROR && tbl) {
        for (ULONG i = 0; i < tbl->NumEntries; i++) {
            auto& r = tbl->Table[i];
            if (r.OperStatus != IfOperStatusUp || r.Type == IF_TYPE_SOFTWARE_LOOPBACK || r.Type == IF_TYPE_TUNNEL) continue;
            down += r.InOctets; up += r.OutOctets;
        }
        FreeMibTable(tbl);
    }
    if (lastNetTick_ && now > lastNetTick_) {
        double dt = (now - lastNetTick_) / 1000.0;
        netDown_ = down >= lastNetDown_ ? (down - lastNetDown_) / 1024.0 / dt : 0; netUp_ = up >= lastNetUp_ ? (up - lastNetUp_) / 1024.0 / dt : 0;
    }
    lastNetDown_ = down; lastNetUp_ = up; lastNetTick_ = now;
    Json d = Json::Object();
    d.set("cpu", Json::Number(std::round(cpu_ * 10) / 10)); d.set("ram", Json::Number(std::round(ram_ * 10) / 10));
    d.set("ramUsedMB", Json::Number(std::round(usedMb))); d.set("ramTotalMB", Json::Number(std::round(totalMb)));
    d.set("netDown", Json::Number(std::round(netDown_ * 10) / 10)); d.set("netUp", Json::Number(std::round(netUp_ * 10) / 10));
    d.set("uptime", Json::Number((double)(GetTickCount64() / 1000)));
    if (cfg_.TelJournal) {
        hotCpu_ = cpu_ > 90 ? hotCpu_ + 1 : 0;
        if (hotCpu_ >= 3 && now > cpuCool_) { addJournal("WARN", "CPU", "load " + std::to_string((int)cpu_) + "%"); cpuCool_ = now + 60000; }
        if (ram_ > 92 && now > ramCool_) { addJournal("WARN", "MEM", "usage " + std::to_string((int)ram_) + "%"); ramCool_ = now + 120000; }
    }
    return msg("sys", d);
}

std::string Telemetry::inputJson(ULONGLONG now) {
    POINT p{}; GetCursorPos(&p);
    HMONITOR mon = MonitorFromPoint(p, MONITOR_DEFAULTTONEAREST);
    MONITORINFO mi{sizeof mi}; int w = 0, h = 0, x = p.x, y = p.y;
    if (mon && GetMonitorInfoW(mon, &mi)) { w = mi.rcMonitor.right - mi.rcMonitor.left; h = mi.rcMonitor.bottom - mi.rcMonitor.top; x = p.x - mi.rcMonitor.left; y = p.y - mi.rcMonitor.top; }
    bool down = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) || (GetAsyncKeyState(VK_RBUTTON) & 0x8000) || (GetAsyncKeyState(VK_MBUTTON) & 0x8000);
    if (down && !wasDown_) { ev_ = "DOWN"; evUntil_ = now + 450; }
    else if (!down && wasDown_) { ev_ = "UP"; evUntil_ = now + 450; }
    else if ((p.x != lastPt_.x || p.y != lastPt_.y) && lastPt_.x >= 0 && now > evUntil_) { ev_ = "MOVE"; evUntil_ = now + 350; }
    if (now > evUntil_) ev_ = "IDLE";
    wasDown_ = down; lastPt_ = p;
    Json d = Json::Object();
    d.set("x", Json::Number(x)); d.set("y", Json::Number(y)); d.set("w", Json::Number(w)); d.set("h", Json::Number(h)); d.set("ev", Json::String(ev_)); d.set("btn", Json::String(down ? "1" : ""));
    return msg("input", d);
}

std::string Telemetry::journalJson(long long fromId, int limit) {
    Json items = Json::Array();
    std::vector<const JEntry*> sel;
    for (auto& j : journal_) if (j.id > fromId) sel.push_back(&j);
    if ((int)sel.size() > limit) sel.erase(sel.begin(), sel.end() - limit);
    for (auto* j : sel) {
        Json e = Json::Object(); e.set("id", Json::Number((double)j->id)); e.set("t", Json::Number((double)j->t)); e.set("lv", Json::String(j->lv)); e.set("src", Json::String(j->src)); e.set("msg", Json::String(j->msg));
        items.a.push_back(e);
    }
    Json d = Json::Object(); d.set("items", items);
    return msg("journal", d);
}

static const char* kIgnore[] = {"ApplicationFrameHost", "TextInputHost", "SearchHost", "ShellExperienceHost", "StartMenuExperienceHost", "LockApp", "GhostStyle", "dwm", "RuntimeBroker"};
static std::string procName(DWORD pid) {
    HANDLE h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid);
    if (!h) return "";
    wchar_t b[MAX_PATH * 2]; DWORD n = (DWORD)std::size(b); std::string r;
    if (QueryFullProcessImageNameW(h, 0, b, &n)) {
        std::wstring p(b, n); size_t s = p.find_last_of(L"\\/"); if (s != std::wstring::npos) p = p.substr(s + 1);
        if (p.size() > 4 && _wcsicmp(p.c_str() + p.size() - 4, L".exe") == 0) p.resize(p.size() - 4);
        r = gs::narrow(p);
    }
    CloseHandle(h); return r;
}
static bool ignored(const std::string& n) { for (auto i : kIgnore) if (_stricmp(n.c_str(), i) == 0) return true; return false; }

void Telemetry::watchFocus() {
    HWND h = GetForegroundWindow(); if (!h) return;
    DWORD pid = 0; GetWindowThreadProcessId(h, &pid);
    std::string n = procName(pid);
    if (n.empty() || ignored(n) || n == lastFocus_) return;
    lastFocus_ = n; addJournal("INFO", "FOCUS", n);
}
void Telemetry::watchWindows() {
    std::vector<std::pair<DWORD, std::string>> now;
    EnumWindows([](HWND h, LPARAM l) -> BOOL {
        auto* v = (std::vector<std::pair<DWORD, std::string>>*)l;
        if (!IsWindowVisible(h) || GetWindowTextLengthW(h) == 0) return TRUE;
        if (GetWindowLongW(h, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) return TRUE;
        int cloaked = 0; if (SUCCEEDED(DwmGetWindowAttribute(h, DWMWA_CLOAKED, &cloaked, sizeof cloaked)) && cloaked) return TRUE;
        DWORD pid = 0; GetWindowThreadProcessId(h, &pid);
        if (!pid) return TRUE;
        for (auto& x : *v) if (x.first == pid) return TRUE;
        std::string n = procName(pid);
        if (!n.empty() && !ignored(n)) v->push_back({pid, n});
        return TRUE;
    }, (LPARAM)&now);
    if (winsPrimed_) {
        int n = 0;
        for (auto& kv : now) { bool had = false; for (auto& o : lastWins_) if (o.first == kv.first) had = true; if (!had && n++ < 4) addJournal("INFO", "START", kv.second); }
        for (auto& o : lastWins_) { bool has = false, sameName = false; for (auto& kv : now) { if (kv.first == o.first) has = true; if (kv.second == o.second) sameName = true; } if (!has && !sameName && n++ < 8) addJournal("INFO", "EXIT", o.second); }
    }
    lastWins_ = now; winsPrimed_ = true;
}

// ---------- погода ----------
std::string Telemetry::weatherCodeText(int c) {
    if (c == 0) return "ясно"; if (c <= 3) return "облачно"; if (c == 45 || c == 48) return "туман"; if (c >= 51 && c <= 57) return "морось";
    if (c >= 61 && c <= 67) return "дождь"; if (c >= 71 && c <= 77) return "снег"; if (c >= 80 && c <= 82) return "ливень"; if (c >= 85 && c <= 86) return "снегопад"; if (c >= 95) return "гроза";
    return "";
}
static long long isoMs(const std::string& s, int offset) {   // "2026-11-26T08:48" (местное время) → мс UTC
    int y, mo, d, h, mi;
    if (std::sscanf(s.c_str(), "%d-%d-%dT%d:%d", &y, &mo, &d, &h, &mi) != 5) return 0;
    y -= mo <= 2; long long era = (y >= 0 ? y : y - 399) / 400; unsigned yoe = (unsigned)(y - era * 400);
    unsigned doy = (153 * (mo + (mo > 2 ? -3 : 9)) + 2) / 5 + d - 1, doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
    long long days = era * 146097 + (long long)doe - 719468;
    return ((days * 24 + h) * 60 + mi) * 60000LL - (long long)offset * 1000LL;
}

void Telemetry::refreshWeather(bool, const void*) {
    if (weatherBusy_) return;
    std::string key = cfg_.WeatherName + "|" + num(cfg_.WeatherLat, 4) + "|" + num(cfg_.WeatherLon, 4);
    weatherKey_ = key;
    if (cfg_.WeatherName.empty()) {
        Json d = Json::Object(); d.set("ok", Json::Boolean(false)); d.set("city", Json::String("")); d.set("temp", Json::Number(0)); d.set("code", Json::Number(0)); d.set("prec", Json::Number(0)); d.set("wind", Json::Number(0));
        d.set("sunrise", Json::Number(0)); d.set("sunset", Json::Number(0)); d.set("err", Json::String("укажите город в настройках"));
        std::lock_guard<std::mutex> g(wmu_); weatherJson_ = msg("weather", d); nWSend_ = 0; return;
    }
    weatherBusy_ = true;
    double lat = cfg_.WeatherLat, lon = cfg_.WeatherLon; std::string name = cfg_.WeatherName;
    workers_.erase(std::remove_if(workers_.begin(), workers_.end(), [](std::thread& t) { return !t.joinable(); }), workers_.end());
    workers_.emplace_back([this, lat, lon, name] {
        Json d = Json::Object(); d.set("city", Json::String(name));
        std::string url = "https://api.open-meteo.com/v1/forecast?latitude=" + num(lat, 4) + "&longitude=" + num(lon, 4) +
            "&current=temperature_2m,weather_code,precipitation,wind_speed_10m&hourly=precipitation_probability&daily=sunrise,sunset&timezone=auto&wind_speed_unit=ms&forecast_days=1";
        std::string body, err; Json r; bool ok = false;
        if (httpGet(url, body, 1000000, err) && gs::jsonParse(body, r) && r.t == Json::Obj) {
            auto* cur = r.get("current");
            if (cur && cur->t == Json::Obj) {
                ok = true;
                int offset = r.num("utc_offset_seconds", 0);
                double prec = 0;
                if (auto* h = r.get("hourly")) {
                    std::string ct = cur->str("time"); std::string hour = ct.substr(0, 13);
                    auto* times = h->get("time"); auto* probs = h->get("precipitation_probability");
                    if (times && probs && times->t == Json::Arr && probs->t == Json::Arr)
                        for (size_t i = 0; i < times->a.size() && i < probs->a.size(); i++) if (times->a[i].s.compare(0, hour.size(), hour) == 0 && probs->a[i].t == Json::Num) { prec = probs->a[i].n; break; }
                }
                long long rise = 0, set = 0;
                if (auto* dd = r.get("daily")) {
                    auto* sr = dd->get("sunrise"); auto* ss = dd->get("sunset");
                    if (sr && sr->t == Json::Arr && !sr->a.empty()) rise = isoMs(sr->a[0].s, offset);
                    if (ss && ss->t == Json::Arr && !ss->a.empty()) set = isoMs(ss->a[0].s, offset);
                }
                auto* t = cur->get("temperature_2m"); auto* w = cur->get("wind_speed_10m");
                d.set("ok", Json::Boolean(true)); d.set("temp", Json::Number(t ? std::round(t->n * 10) / 10 : 0)); d.set("code", Json::Number(cur->num("weather_code")));
                d.set("prec", Json::Number(prec)); d.set("wind", Json::Number(w ? std::round(w->n * 10) / 10 : 0)); d.set("sunrise", Json::Number((double)rise)); d.set("sunset", Json::Number((double)set)); d.set("err", Json::String(""));
            }
        }
        if (!ok) { d.set("ok", Json::Boolean(false)); d.set("temp", Json::Number(0)); d.set("code", Json::Number(0)); d.set("prec", Json::Number(0)); d.set("wind", Json::Number(0)); d.set("sunrise", Json::Number(0)); d.set("sunset", Json::Number(0)); d.set("err", Json::String("нет связи с сервисом погоды")); log("погода: " + err); }
        { std::lock_guard<std::mutex> g(wmu_); weatherJson_ = msg("weather", d); nWSend_ = 0; }
        weatherBusy_ = false;
    });
}

void Telemetry::findCity(const std::string& query) {
    if (cityBusy_) return;
    std::string q = query; while (!q.empty() && q.back() == ' ') q.pop_back();
    if (q.size() < 2 || q.size() > 80) { std::lock_guard<std::mutex> g(wmu_); cityErr_ = "Введите название города."; cityReady_ = true; cityName_.clear(); return; }
    cityBusy_ = true;
    workers_.emplace_back([this, q] {
        std::string enc; char b[4];
        for (unsigned char c : q) { if (isalnum(c) || c == '-' || c == '_' || c == '.') enc += (char)c; else { std::snprintf(b, sizeof b, "%%%02X", c); enc += b; } }
        std::string body, err; Json r; std::string name, e2; double la = 0, lo = 0;
        if (httpGet("https://geocoding-api.open-meteo.com/v1/search?count=1&language=ru&format=json&name=" + enc, body, 200000, err) && gs::jsonParse(body, r)) {
            auto* res = r.get("results");
            if (res && res->t == Json::Arr && !res->a.empty()) {
                auto& x = res->a[0]; name = x.str("name", q); std::string country = x.str("country");
                if (!country.empty()) name += ", " + country;
                if (auto* a = x.get("latitude")) la = a->n; if (auto* a = x.get("longitude")) lo = a->n;
            } else e2 = "Такой город не найден.";
        } else e2 = "Нет связи с сервисом поиска городов.";
        { std::lock_guard<std::mutex> g(wmu_); cityName_ = name; cityLat_ = la; cityLon_ = lo; cityErr_ = e2; cityReady_ = true; }
        cityBusy_ = false;
    });
}
bool Telemetry::takeCity(std::string& name, double& lat, double& lon, std::string& error) {
    std::lock_guard<std::mutex> g(wmu_);
    if (!cityReady_) return false;
    cityReady_ = false; name = cityName_; lat = cityLat_; lon = cityLon_; error = cityErr_; return true;
}

// ---------- главный такт ----------
void Telemetry::pump(const std::function<void(const char*, const std::string&)>& send) {
    if (wanted_.empty()) return;
    ULONGLONG now = GetTickCount64();
    auto want = [&](const char* k) { return wanted_.count(k) > 0; };
    if (want("audio") && cfg_.TelAudio && now >= nAudio_) {
        nAudio_ = now + 40; send("audio", audio_.snapshot());
        int v = (int)std::lround(audio_.volume() * 100);
        if (cfg_.TelJournal && lastVol_ >= 0 && v != lastVol_ && now - volSince_ > 2000) { addJournal("INFO", "VOL", std::to_string(v) + "%"); volSince_ = now; }
        lastVol_ = v;
    }
    if (want("input") && now >= nInput_) { nInput_ = now + 50; send("input", inputJson(now)); }
    if (now >= nSys_ && (want("sys") || (want("journal") && cfg_.TelJournal))) {
        nSys_ = now + 1000; std::string m = sysJson(now);
        if (want("sys")) send("sys", m);
    }
    if (want("media") && cfg_.TelAudio && now >= nMedia_) {
        nMedia_ = now + 1000;
        Json d = Json::Object(); d.set("has", Json::Boolean(false)); d.set("title", Json::String("")); d.set("artist", Json::String("")); d.set("album", Json::String(""));
        d.set("app", Json::String("")); d.set("playing", Json::Boolean(false)); d.set("pos", Json::Number(0)); d.set("dur", Json::Number(0));
        send("media", msg("media", d));
    }
    if (want("journal") && cfg_.TelJournal) {
        if (now >= nFocus_) { nFocus_ = now + 500; watchFocus(); }
        if (now >= nWin_) { nWin_ = now + 3000; watchWindows(); }
        if (journalId_ > journalSent_) { std::string m = journalJson(journalSent_, 40); journalSent_ = journalId_; send("journal", m); }
    }
    if (want("weather")) {
        if (now >= nWeather_) { nWeather_ = now + 600000; refreshWeather(true, nullptr); }
        std::lock_guard<std::mutex> g(wmu_);
        if (!weatherJson_.empty() && now >= nWSend_) { nWSend_ = now + 30000; send("weather", weatherJson_); }
    }
}

std::vector<std::string> Telemetry::resync(const std::set<std::string>& kinds) {
    std::vector<std::string> out;
    if (kinds.count("journal") && cfg_.TelJournal) out.push_back(journalJson(std::max<long long>(0, journalId_ - 12), 12));
    if (kinds.count("weather")) { std::lock_guard<std::mutex> g(wmu_); if (!weatherJson_.empty()) out.push_back(weatherJson_); }
    return out;
}

}  // namespace wp
