#include "config.h"
#include <algorithm>
#include <cstdlib>
#include <string>

std::wstring ConfigPath() { return DataDir() + L"\\taskbar-plus-engine.cfg"; }

static int Clamp(long v, int lo, int hi) { return (int)std::min<long>(hi, std::max<long>(lo, v)); }

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

static int GroupIndex(const std::string& n) { return n == "widgets" ? 0 : n == "system" ? 1 : n == "apps" ? 2 : -1; }
static int RuleKind(const std::string& n) { return n == "bgfill" ? 1 : n == "bgstroke" ? 2 : n == "app" ? 3 : n == "widgets" ? 4 : n == "system" ? 5 : n == "pill" ? 6 : 0; }

// «радиус;цвет;прозрачность;режим;скрыт» — пустое поле означает «как у всех»
static IslandStyle ParseStyle(const std::string& v)
{
    IslandStyle st;
    auto f = Split(v, ';');
    f.resize(5);
    if (!f[0].empty()) st.radius = Clamp(strtol(f[0].c_str(), nullptr, 10), 0, 100);
    if (!f[1].empty()) st.color = (int)(strtoul(f[1].c_str(), nullptr, 16) & 0xFFFFFF);
    if (!f[2].empty()) st.opacity = Clamp(strtol(f[2].c_str(), nullptr, 10), 0, 100);
    if (!f[3].empty()) st.mode = Clamp(strtol(f[3].c_str(), nullptr, 10), 0, 3);
    st.hidden = f[4] == "1";
    return st;
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
        else if (key == "bgMode") c.bgMode = Clamp(n, 0, 3);
        else if (key == "color") c.color = (unsigned)(strtoul(val.c_str(), nullptr, 16) & 0xFFFFFF);
        else if (key == "opacity") c.opacity = Clamp(n, 0, 100);
        else if (key == "shape") c.shape = Clamp(n, 0, 3);
        else if (key == "radius") c.radius = Clamp(n, 0, 40);
        else if (key == "side") c.side = Clamp(n, 0, 800);
        else if (key == "gap") c.gap = Clamp(n, 0, 30);
        else if (key == "hideStroke") c.hideStroke = n != 0;
        else if (key == "keepEdges") c.keepEdges = n != 0;
        else if (key == "unifySystem") c.unifySystem = n != 0;
        else if (key == "islandMode") c.islandMode = Clamp(n, 0, 1);
        else if (key == "islandStyle") c.islandStyle = Clamp(n, 0, 1);
        else if (key == "islandPad") c.islandPad = Clamp(n, 0, 40);
        else if (key == "islandGap") c.islandGap = Clamp(n, 0, 40);
        else if (key == "dump") c.dump = (int)n;
        else if (key == "edgeMargin") c.edgeMargin = Clamp(n, 0, 400);
        else if (key.rfind("zone.", 0) == 0)
        {
            int g = GroupIndex(key.substr(5));
            if (g >= 0) c.zone[g] = Clamp(n, 0, 3);
        }
        else if (key == "order")
        {
            // порядок групп слева направо; если список неполный или кривой — остаётся порядок по умолчанию
            int ord[3], cnt = 0;
            bool used[3] = { false, false, false };
            for (auto& name : Split(val, ','))
            {
                int g = GroupIndex(name);
                if (g >= 0 && !used[g] && cnt < 3) { used[g] = true; ord[cnt++] = g; }
            }
            if (cnt == 3) { c.order[0] = ord[0]; c.order[1] = ord[1]; c.order[2] = ord[2]; }
        }
        else if (key.rfind("sty.", 0) == 0) { if (c.styles.size() < 200) c.styles[key.substr(4)] = ParseStyle(val); }
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
    }
    out = c;
    return true;
}
