#include "config.h"
#include <algorithm>
#include <cstdlib>
#include <string>

std::wstring ConfigPath() { return DataDir() + L"\\taskbar-plus-engine.cfg"; }

static std::vector<std::string> Split(const std::string& s, char sep)
{
    std::vector<std::string> out;
    size_t pos = 0;
    while (true)
    {
        size_t e = s.find(sep, pos);
        if (e == std::string::npos) { out.push_back(s.substr(pos)); break; }
        out.push_back(s.substr(pos, e - pos));
        pos = e + 1;
    }
    return out;
}

static int RuleKind(const std::string& n)
{
    return n == "bgfill" ? 1 : n == "bgstroke" ? 2 : n == "app" ? 3 : n == "widgets" ? 4 : n == "system" ? 5 : n == "frame" ? 8 : 0;
}

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
        else if (key == "hideStroke") c.hideStroke = n != 0;
        else if (key == "dump") c.dump = (int)n;
        else if (key.rfind("rule.", 0) == 0)
        {
            auto f = Split(val, '|');
            if (f.size() >= 3 && c.rules.size() < 100)
            {
                Rule r;
                r.kind = RuleKind(f[0]);
                r.type = f[1];
                r.name = f[2];
                if (r.kind != 0) { c.rules.push_back(r); c.rulesSig += val; c.rulesSig += '\n'; }
            }
        }
        // остальные ключи (старые версии плагина писали форму, отступы, зоны) игнорируются
    }
    out = c;
    return true;
}
