#include "common.h"
#include <cstdarg>
#include <cstdio>
#include <mutex>

std::wstring ReadEnv(const wchar_t* name)
{
    wchar_t buf[MAX_PATH * 2];
    DWORD n = GetEnvironmentVariableW(name, buf, (DWORD)(sizeof(buf) / sizeof(buf[0])));
    if (n == 0 || n >= sizeof(buf) / sizeof(buf[0])) return L"";
    return std::wstring(buf, n);
}

std::wstring DataDir()
{
    static std::wstring dir;
    if (dir.empty())
    {
        auto base = ReadEnv(L"APPDATA");
        if (base.empty()) base = ReadEnv(L"TEMP");
        dir = base + L"\\BattleLauncher";
        CreateDirectoryW(dir.c_str(), nullptr);
    }
    return dir;
}

std::string ToUtf8(const std::wstring& s)
{
    if (s.empty()) return {};
    int n = WideCharToMultiByte(CP_UTF8, 0, s.data(), (int)s.size(), nullptr, 0, nullptr, nullptr);
    std::string out(n, '\0');
    WideCharToMultiByte(CP_UTF8, 0, s.data(), (int)s.size(), out.data(), n, nullptr, nullptr);
    return out;
}

void WriteTextAtomic(const std::wstring& path, const std::string& utf8)
{
    auto tmp = path + L".tmp";
    HANDLE h = CreateFileW(tmp.c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (h == INVALID_HANDLE_VALUE) return;
    DWORD written = 0;
    BOOL ok = WriteFile(h, utf8.data(), (DWORD)utf8.size(), &written, nullptr);
    CloseHandle(h);
    if (!ok || !MoveFileExW(tmp.c_str(), path.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) DeleteFileW(tmp.c_str());
}

static std::mutex g_logLock;

void Log(const wchar_t* fmt, ...)
{
    try
    {
        wchar_t msg[1024];
        va_list ap;
        va_start(ap, fmt);
        _vsnwprintf_s(msg, _TRUNCATE, fmt, ap);
        va_end(ap);

        SYSTEMTIME t;
        GetLocalTime(&t);
        wchar_t line[1200];
        _snwprintf_s(line, _TRUNCATE, L"%02d:%02d:%02d.%03d  %s\r\n", t.wHour, t.wMinute, t.wSecond, t.wMilliseconds, msg);

        std::lock_guard<std::mutex> lock(g_logLock);
        auto path = DataDir() + L"\\taskbar-plus-engine.log";
        WIN32_FILE_ATTRIBUTE_DATA info{};
        if (GetFileAttributesExW(path.c_str(), GetFileExInfoStandard, &info) && info.nFileSizeLow > 200 * 1024)
            DeleteFileW(path.c_str()); // журнал не должен расти бесконечно
        HANDLE h = CreateFileW(path.c_str(), FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (h == INVALID_HANDLE_VALUE) return;
        auto utf8 = ToUtf8(line);
        DWORD written = 0;
        WriteFile(h, utf8.data(), (DWORD)utf8.size(), &written, nullptr);
        CloseHandle(h);
    }
    catch (...) { /* журнал не обязателен */ }
}
