// Картинки курсоров и файлы .cur: рисуем форму в нужный размер, читаем системные указатели и перекрашиваем, собираем .cur с PNG внутри.
#pragma once
#include <string>
#include <vector>
#include "cursor_settings.hpp"

namespace sp {

struct CurImg {
    int w = 0, h = 0, hx = 0, hy = 0;
    std::vector<unsigned char> bgra;       // обычная (не «предумноженная») прозрачность
};

bool renderShapeImg(const std::string& shape, int px, Rgb fill, Rgb line, CurImg& out);        // форма из cursor_shapes
bool loadSystemCursorImg(const std::wstring& file, int px, CurImg& out);                       // .cur/.ani из Windows (любого вида, в том числе без прозрачности)
void tintImg(CurImg& img, Rgb fill, Rgb line);                                                  // светлое → цвет курсора, тёмное → обводка
std::string buildCur(const std::vector<CurImg>& sizes);                                         // файл .cur: по картинке на размер, каждая как PNG
bool encodePng(const CurImg& img, std::string& out);

}  // namespace sp
