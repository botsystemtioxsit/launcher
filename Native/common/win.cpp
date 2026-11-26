#include "win.hpp"
#include <shlobj.h>
#include <shellapi.h>
#include <shellscalingapi.h>
#include <vector>

namespace gs {

std::wstring widen(const std::string& s) {
    if (s.empty()) return {};
    int n = MultiByteToWideChar(CP_UTF8, 0, s.data(), (int)s.size(), nullptr, 0);
    std::wstring w(n, 0);
    MultiByteToWideChar(CP_UTF8, 0, s.data(), (int)s.size(), w.data(), n);
    return w;
}
std::string narrow(const std::wstring& w) {
    if (w.empty()) return {};
    int n = WideCharToMultiByte(CP_UTF8, 0, w.data(), (int)w.size(), nullptr, 0, nullptr, nullptr);
    std::string s(n, 0);
    WideCharToMultiByte(CP_UTF8, 0, w.data(), (int)w.size(), s.data(), n, nullptr, nullptr);
    return s;
}

std::wstring appDataDir() {
    wchar_t buf[MAX_PATH * 2] = {};
    DWORD n = GetEnvironmentVariableW(L"APPDATA", buf, (DWORD)std::size(buf));
    std::wstring base = n ? std::wstring(buf, n) : L".";
    return base + L"\\BattleLauncher";
}

std::wstring exePath() {
    wchar_t buf[MAX_PATH * 2] = {};
    DWORD n = GetModuleFileNameW(nullptr, buf, (DWORD)std::size(buf));
    return std::wstring(buf, n);
}

bool makeDirs(const std::wstring& dir) {
    for (size_t i = 3; i <= dir.size(); i++)
        if (i == dir.size() || dir[i] == L'\\' || dir[i] == L'/') {
            std::wstring part = dir.substr(0, i);
            CreateDirectoryW(part.c_str(), nullptr);
        }
    DWORD a = GetFileAttributesW(dir.c_str());
    return a != INVALID_FILE_ATTRIBUTES && (a & FILE_ATTRIBUTE_DIRECTORY);
}

bool readFile(const std::wstring& path, std::string& out) {
    HANDLE h = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (h == INVALID_HANDLE_VALUE) return false;
    LARGE_INTEGER sz{};
    GetFileSizeEx(h, &sz);
    if (sz.QuadPart > (64 << 20)) { CloseHandle(h); return false; }
    out.resize((size_t)sz.QuadPart);
    DWORD got = 0;
    bool ok = out.empty() || ReadFile(h, out.data(), (DWORD)out.size(), &got, nullptr);
    CloseHandle(h);
    if (ok) out.resize(got);
    return ok;
}

bool writeFileAtomic(const std::wstring& path, const std::string& data) {
    std::wstring tmp = path + L".tmp";
    HANDLE h = CreateFileW(tmp.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (h == INVALID_HANDLE_VALUE) return false;
    DWORD put = 0;
    bool ok = data.empty() || WriteFile(h, data.data(), (DWORD)data.size(), &put, nullptr);
    CloseHandle(h);
    if (!ok || put != data.size()) { DeleteFileW(tmp.c_str()); return false; }
    if (!MoveFileExW(tmp.c_str(), path.c_str(), MOVEFILE_REPLACE_EXISTING)) { DeleteFileW(tmp.c_str()); return false; }
    return true;
}

void setAutostart(const wchar_t* name, const std::wstring& args, bool on) {
    HKEY k;
    if (RegCreateKeyExW(HKEY_CURRENT_USER, L"Software\\Microsoft\\Windows\\CurrentVersion\\Run", 0, nullptr, 0, KEY_SET_VALUE, nullptr, &k, nullptr) != ERROR_SUCCESS) return;
    if (on) {
        std::wstring v = L"\"" + exePath() + L"\" " + args;
        RegSetValueExW(k, name, 0, REG_SZ, (const BYTE*)v.c_str(), (DWORD)((v.size() + 1) * sizeof(wchar_t)));
    } else RegDeleteValueW(k, name);
    RegCloseKey(k);
}

SingleInstance::~SingleInstance() { stop(); }

bool SingleInstance::acquire(const std::wstring& mutexName, const std::wstring& showEvent, const std::wstring& quitEvent, std::function<void()> onShow, std::function<void()> onQuit) {
    mutex_ = CreateMutexW(nullptr, TRUE, mutexName.c_str());
    if (!mutex_) return false;
    if (GetLastError() == ERROR_ALREADY_EXISTS) { CloseHandle(mutex_); mutex_ = nullptr; signal(showEvent); return false; }
    onShow_ = std::move(onShow); onQuit_ = std::move(onQuit);
    show_ = CreateEventW(nullptr, FALSE, FALSE, showEvent.c_str());
    quit_ = CreateEventW(nullptr, FALSE, FALSE, quitEvent.c_str());
    stop_ = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    th_[0] = CreateThread(nullptr, 0, Loop, this, 0, nullptr);
    return true;
}

DWORD WINAPI SingleInstance::Loop(LPVOID p) {
    auto* self = (SingleInstance*)p;
    HANDLE hs[3] = {self->stop_, self->show_, self->quit_};
    for (;;) {
        DWORD r = WaitForMultipleObjects(3, hs, FALSE, INFINITE);
        if (r == WAIT_OBJECT_0 || r == WAIT_FAILED) return 0;
        if (r == WAIT_OBJECT_0 + 1 && self->onShow_) self->onShow_();
        if (r == WAIT_OBJECT_0 + 2 && self->onQuit_) self->onQuit_();
    }
}

void SingleInstance::signal(const std::wstring& eventName) {
    HANDLE e = OpenEventW(EVENT_MODIFY_STATE, FALSE, eventName.c_str());
    if (e) { SetEvent(e); CloseHandle(e); }
}

void SingleInstance::stop() {
    if (stop_) SetEvent(stop_);
    if (th_[0]) { WaitForSingleObject(th_[0], 1500); CloseHandle(th_[0]); th_[0] = nullptr; }
    for (HANDLE* h : {&show_, &quit_, &stop_}) if (*h) { CloseHandle(*h); *h = nullptr; }
    if (mutex_) { ReleaseMutex(mutex_); CloseHandle(mutex_); mutex_ = nullptr; }
}

bool fullscreenBusy() {
    typedef int (WINAPI* Fn)(int*);
    static Fn fn = (Fn)GetProcAddress(LoadLibraryW(L"shell32.dll"), "SHQueryUserNotificationState");
    if (!fn) return false;
    int s = 0;
    // 2 — полноэкранное приложение, 3 — игра (Direct3D), 4 — презентация
    return fn(&s) == 0 && (s == 2 || s == 3 || s == 4);
}

}  // namespace gs
