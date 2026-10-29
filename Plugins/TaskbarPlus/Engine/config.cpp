#include "config.h"
#include <algorithm>
#include <cstdlib>
#include <string>

std::wstring ConfigPath() { return DataDir() + L"\\taskbar-plus-engine.cfg"; }

static int Clamp(long v, int lo, int hi) { return (int)std::min<long>(hi, std::max<long>(lo, v)); }

bool LoadConfig(Config& out, FILETIME& mtime)
{
    auto path = ConfigPath();
    WIN32_FILE_ATTRIBUTE_DATA info{};
    if (!GetFileAttributesExW(path.c_str(), GetFileExInfoStandard, &info)) return false;
    mtime = info.ftLastWriteTime;

    HANDLE h = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (h == INVALID_HANDLE_VALUE) return false;
    std::string text;
    char buf[4096];
    DWORD got = 0;
    while (ReadFile(h, buf, sizeof(buf), &got, nullptr) && got > 0 && text.size() < 64 * 1024) text.append(buf, got);
    CloseHandle(h);

    Config c;
    size_t pos = 0;
    while (pos < text.size())
    {
        size_t end = text.find('\n', pos);
        if (end == std::string::npos) end = text.size();
        std::string line = text.substr(pos, end - pos);
        pos = end + 1;
        while (!line.empty() && (line.back() == '\r' || line.back() == ' ')) line.pop_back();
        auto eq = line.find('=');
        if (eq == std::string::npos) continue;
        auto key = line.substr(0, eq);
        auto val = line.substr(eq + 1);
        long n = strtol(val.c_str(), nullptr, 10);
        if (key == "enabled") c.enabled = n != 0;
        else if (key == "bgMode") c.bgMode = Clamp(n, 0, 3);
        else if (key == "color") c.color = (unsigned)(strtoul(val.c_str(), nullptr, 16) & 0xFFFFFF);
        else if (key == "opacity") c.opacity = Clamp(n, 0, 100);
        else if (key == "shape") c.shape = Clamp(n, 0, 3);
        else if (key == "radius") c.radius = Clamp(n, 0, 40);
        else if (key == "side") c.side = Clamp(n, 0, 800);
        else if (key == "gap") c.gap = Clamp(n, 0, 30);
        else if (key == "hideStroke") c.hideStroke = n != 0;
        else if (key == "keepEdges") c.keepEdges = n != 0;
        else if (key == "islandMode") c.islandMode = Clamp(n, 0, 1);
        else if (key == "islandStyle") c.islandStyle = Clamp(n, 0, 1);
        else if (key == "islandPad") c.islandPad = Clamp(n, 0, 40);
        else if (key == "islandGap") c.islandGap = Clamp(n, 0, 40);
        else if (key == "dump") c.dump = (int)n;
    }
    out = c;
    return true;
}
