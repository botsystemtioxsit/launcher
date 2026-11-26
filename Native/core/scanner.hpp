#pragma once
#include <vector>
#include <windows.h>
#include "surface.hpp"
namespace sp {
// Какие окна сейчас на экране и какие куски их верхнего края видны. vy — верх виртуального рабочего стола.
std::vector<Surf> scanWindows(DWORD ownPid, int vy);
}
