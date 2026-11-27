// Параметры панели задач из реестра текущего пользователя — те же, что Windows хранит в «Параметры → Персонализация → Панель задач».
// Права администратора не нужны. Перед первым изменением значение запоминается; «Вернуть как было» возвращает всё.
#pragma once
#include <string>
#include <vector>

namespace tb {

struct RegOption {
    const char* id; const char* title; const char* hint; const wchar_t* key; const wchar_t* name;
    std::vector<int> values; std::vector<const char*> labels; int def; bool win11Only, win10Only, needsRestart;
};

const std::vector<RegOption>& regOptions();
bool regAvailable(const RegOption& o);
int regCurrent(const RegOption& o);
std::string regSet(const RegOption& o, int value);          // текст ошибки или пусто
bool autoHideGet();
std::string autoHideSet(bool on);
bool animationsOn();
void animationsEnable();
std::string explorerRestart();
int regReset(std::string& error);                           // сколько вернули
void notifySettingsChanged();

}  // namespace tb
