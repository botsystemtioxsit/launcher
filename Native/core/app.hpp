// Приложение «Ghost Style»: одно окно со вкладками-модулями, один значок в трее, один прозрачный слой поверх экрана.
#pragma once
#include <memory>
#include <vector>
#include "module.hpp"

namespace sp {

struct AppInfo {
    const wchar_t* name = L"Ghost Style";
    const wchar_t* autostartName = L"GhostStyle";
    const wchar_t* mutexName = L"GhostStyle.SingleInstance.GhostShell";
    const wchar_t* showEvent = L"GhostStyle.Show.GhostShell";
    const wchar_t* quitEvent = L"GhostShell.Plugin.Quit.ghost-style";   // имя ждёт лаунчер: "GhostShell.Plugin.Quit." + id приложения
    const char* locText = nullptr;                                     // словарь переводов приложения
};

int runApp(const AppInfo& info, std::vector<std::unique_ptr<Module>>& modules, int argc, wchar_t** argv);

}  // namespace sp
