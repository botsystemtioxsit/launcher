// Запись курсора в Windows (реестр + файлы .cur) и возврат прежнего.
#pragma once
#include <string>
#include "cursor_settings.hpp"

namespace sp {

// Возвращает "" при успехе или текст ошибки.
std::string applyCursors(const CursorSettings& s);
std::string revertCursors();
// Размер указателя в пикселях по настройке «Размер» (32–256, шаг 16)
int baseSizePx(const CursorSettings& s);

}  // namespace sp
