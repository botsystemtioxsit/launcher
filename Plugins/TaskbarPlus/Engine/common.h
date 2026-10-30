#pragma once
// Общие мелочи движка Taskbar Plus (DLL, которая подключается к панели задач Windows 11 внутри Проводника).
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <string>

#define GS_ENGINE_VERSION L"5"

std::wstring ReadEnv(const wchar_t* name);
std::wstring DataDir();                       // %APPDATA%\BattleLauncher — там же лежат настройки плагина
std::string ToUtf8(const std::wstring& s);
void Log(const wchar_t* fmt, ...);            // строка в taskbar-plus-engine.log (файл сам обрезается)
void WriteTextAtomic(const std::wstring& path, const std::string& utf8);
