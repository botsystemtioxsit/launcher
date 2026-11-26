// Общие мелочи Windows для нативных плагинов: пути, файлы, автозапуск, одна копия, выход по просьбе лаунчера.
#pragma once
#include <string>
#include <functional>
#include <windows.h>

namespace gs {

std::wstring widen(const std::string& s);
std::string narrow(const std::wstring& s);

std::wstring appDataDir();                      // %APPDATA%\BattleLauncher
std::wstring exePath();
bool readFile(const std::wstring& path, std::string& out);
bool writeFileAtomic(const std::wstring& path, const std::string& data);
bool makeDirs(const std::wstring& dir);

void setAutostart(const wchar_t* name, const std::wstring& args, bool on);

// Одна копия программы: повторный запуск просит первую показать окно. Лаунчер просит выйти событием "GhostShell.Plugin.Quit.<id>".
class SingleInstance {
public:
    ~SingleInstance();
    // true — мы первые. onShow/onQuit вызываются из отдельного потока.
    bool acquire(const std::wstring& mutexName, const std::wstring& showEvent, const std::wstring& quitEvent, std::function<void()> onShow, std::function<void()> onQuit);
    static void signal(const std::wstring& eventName);   // «разбудить» уже работающую копию
    void stop();
private:
    HANDLE mutex_ = nullptr, show_ = nullptr, quit_ = nullptr, stop_ = nullptr;
    HANDLE th_[2] = {nullptr, nullptr};
    std::function<void()> onShow_, onQuit_;
    static DWORD WINAPI Loop(LPVOID p);
};

bool fullscreenBusy();                          // игра, видео, презентация на весь экран
}  // namespace gs
