// Языки интерфейса (русский / English / Deutsch): словарь «русская строка ⇒ English ⇒ Deutsch», те же файлы .loc, что у C#-версий.
#pragma once
#include <string>

namespace gs {
void locInit(const char* appLocText, int argc, wchar_t** argv);   // appLocText — содержимое Loc/app.loc приложения
const char* L(const char* ru);                                      // перевод (UTF-8); если строки нет в словаре — как есть
int locLang();                                                      // 0 русский, 1 English, 2 Deutsch
void locSet(int lang, bool remember);
}
