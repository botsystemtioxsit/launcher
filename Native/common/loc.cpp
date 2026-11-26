#include "loc.hpp"
#include "win.hpp"
#include <unordered_map>
#include <vector>
#include <cstring>

namespace gs {
namespace {
std::unordered_map<std::string, std::vector<std::string>> dict;
int lang = 0;
std::string langFile() { return narrow(appDataDir()) + "\\language.txt"; }

void load(const char* text) {
    if (!text) return;
    std::string src(text);
    size_t pos = 0;
    while (pos < src.size()) {
        size_t e = src.find('\n', pos);
        if (e == std::string::npos) e = src.size();
        std::string line = src.substr(pos, e - pos);
        pos = e + 1;
        if (!line.empty() && line.back() == '\r') line.pop_back();
        if (line.empty() || line[0] == '#') continue;
        const std::string sep = "\xE2\x87\x92";   // ⇒
        std::vector<std::string> parts;
        size_t st = 0;
        for (;;) {
            size_t k = line.find(sep, st);
            std::string p = line.substr(st, k == std::string::npos ? std::string::npos : k - st);
            while (!p.empty() && p.back() == ' ') p.pop_back();
            while (!p.empty() && p.front() == ' ') p.erase(p.begin());
            parts.push_back(p);
            if (k == std::string::npos) break;
            st = k + sep.size();
        }
        if (parts.size() < 3) continue;
        // перевод строки в словаре записан как \n
        for (auto& p : parts) { size_t q; while ((q = p.find("\\n")) != std::string::npos) p.replace(q, 2, "\n"); }
        dict[parts[0]] = {parts[0], parts[1], parts[2]};
    }
}
}  // namespace

void locInit(const char* appLocText, int argc, wchar_t** argv) {
    extern const char* kCommonLoc;
    load(kCommonLoc);
    load(appLocText);
    lang = -1;
    for (int i = 1; i < argc; i++) {
        std::wstring a = argv[i];
        if (a == L"--lang=ru") lang = 0; else if (a == L"--lang=en") lang = 1; else if (a == L"--lang=de") lang = 2;
    }
    if (lang < 0) {
        std::string t;
        if (readFile(widen(langFile()), t)) {
            if (t.rfind("en", 0) == 0) lang = 1; else if (t.rfind("de", 0) == 0) lang = 2; else if (t.rfind("ru", 0) == 0) lang = 0;
        }
    }
    if (lang < 0) {
        wchar_t name[LOCALE_NAME_MAX_LENGTH] = {};
        GetUserDefaultLocaleName(name, LOCALE_NAME_MAX_LENGTH);
        std::wstring n = name;
        if (n.rfind(L"ru", 0) == 0 || n.rfind(L"uk", 0) == 0 || n.rfind(L"be", 0) == 0 || n.rfind(L"kk", 0) == 0) lang = 0;
        else if (n.rfind(L"de", 0) == 0) lang = 2;
        else lang = 1;
    }
}

const char* L(const char* ru) {
    if (lang == 0) return ru;
    auto it = dict.find(ru);
    if (it == dict.end()) return ru;
    const std::string& t = it->second[lang];
    return t.empty() ? ru : t.c_str();
}

int locLang() { return lang; }

void locSet(int l, bool remember) {
    lang = l;
    if (remember) { makeDirs(appDataDir()); writeFileAtomic(widen(langFile()), l == 1 ? "en" : l == 2 ? "de" : "ru"); }
}

}  // namespace gs
