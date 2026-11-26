// Маленький JSON: читает и пишет настройки плагинов (те же файлы, что у C#-версий, формат один и тот же).
#pragma once
#include <string>
#include <vector>
#include <utility>
#include <cstdlib>
#include <cstdio>
#include <cmath>

namespace gs {

struct Json {
    enum Type { Null, Bool, Num, Str, Arr, Obj } t = Null;
    bool b = false;
    double n = 0;
    std::string s;
    std::vector<Json> a;
    std::vector<std::pair<std::string, Json>> o;

    static Json Boolean(bool v) { Json j; j.t = Bool; j.b = v; return j; }
    static Json Number(double v) { Json j; j.t = Num; j.n = v; return j; }
    static Json String(std::string v) { Json j; j.t = Str; j.s = std::move(v); return j; }
    static Json Object() { Json j; j.t = Obj; return j; }
    static Json Array() { Json j; j.t = Arr; return j; }

    const Json* get(const std::string& k) const {
        if (t != Obj) return nullptr;
        for (auto& p : o) if (p.first == k) return &p.second;
        return nullptr;
    }
    void set(const std::string& k, Json v) {
        for (auto& p : o) if (p.first == k) { p.second = std::move(v); return; }
        o.emplace_back(k, std::move(v));
    }
    std::string str(const std::string& k, const std::string& def = "") const { auto* j = get(k); return j && j->t == Str ? j->s : def; }
    int num(const std::string& k, int def = 0) const { auto* j = get(k); return j && j->t == Num ? (int)std::lround(j->n) : def; }
    bool boolean(const std::string& k, bool def = false) const { auto* j = get(k); return j && j->t == Bool ? j->b : def; }
};

namespace detail {
struct P {
    const std::string& s; size_t i = 0; bool ok = true; int depth = 0;
    explicit P(const std::string& src) : s(src) {}
    void ws() { while (i < s.size() && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++; }
    static void utf8(std::string& out, unsigned cp) {
        if (cp < 0x80) out += (char)cp;
        else if (cp < 0x800) { out += (char)(0xC0 | (cp >> 6)); out += (char)(0x80 | (cp & 63)); }
        else if (cp < 0x10000) { out += (char)(0xE0 | (cp >> 12)); out += (char)(0x80 | ((cp >> 6) & 63)); out += (char)(0x80 | (cp & 63)); }
        else { out += (char)(0xF0 | (cp >> 18)); out += (char)(0x80 | ((cp >> 12) & 63)); out += (char)(0x80 | ((cp >> 6) & 63)); out += (char)(0x80 | (cp & 63)); }
    }
    unsigned hex4() {
        unsigned v = 0;
        for (int k = 0; k < 4; k++) {
            if (i >= s.size()) { ok = false; return 0; }
            char c = s[i++]; v <<= 4;
            if (c >= '0' && c <= '9') v |= c - '0'; else if (c >= 'a' && c <= 'f') v |= c - 'a' + 10; else if (c >= 'A' && c <= 'F') v |= c - 'A' + 10; else ok = false;
        }
        return v;
    }
    std::string str() {
        std::string out;
        i++;  // "
        while (i < s.size() && s[i] != '"') {
            char c = s[i++];
            if (c != '\\') { out += c; continue; }
            if (i >= s.size()) { ok = false; break; }
            char e = s[i++];
            switch (e) {
                case 'n': out += '\n'; break; case 't': out += '\t'; break; case 'r': out += '\r'; break;
                case 'b': out += '\b'; break; case 'f': out += '\f'; break;
                case 'u': { unsigned cp = hex4(); if (cp >= 0xD800 && cp < 0xDC00 && i + 1 < s.size() && s[i] == '\\' && s[i + 1] == 'u') { i += 2; unsigned lo = hex4(); cp = 0x10000 + ((cp - 0xD800) << 10) + (lo - 0xDC00); } utf8(out, cp); break; }
                default: out += e;
            }
        }
        if (i >= s.size()) ok = false; else i++;
        return out;
    }
    Json val() {
        ws();
        if (i >= s.size() || ++depth > 64) { ok = false; return {}; }
        Json j;
        char c = s[i];
        if (c == '{') {
            j.t = Json::Obj; i++; ws();
            if (i < s.size() && s[i] == '}') i++;
            else for (;;) {
                ws(); if (i >= s.size() || s[i] != '"') { ok = false; break; }
                std::string k = str(); ws();
                if (i >= s.size() || s[i] != ':') { ok = false; break; }
                i++; j.o.emplace_back(k, val()); ws();
                if (i < s.size() && s[i] == ',') { i++; continue; }
                if (i < s.size() && s[i] == '}') { i++; break; }
                ok = false; break;
            }
        } else if (c == '[') {
            j.t = Json::Arr; i++; ws();
            if (i < s.size() && s[i] == ']') i++;
            else for (;;) {
                j.a.push_back(val()); ws();
                if (i < s.size() && s[i] == ',') { i++; continue; }
                if (i < s.size() && s[i] == ']') { i++; break; }
                ok = false; break;
            }
        } else if (c == '"') { j.t = Json::Str; j.s = str(); }
        else if (s.compare(i, 4, "true") == 0) { j.t = Json::Bool; j.b = true; i += 4; }
        else if (s.compare(i, 5, "false") == 0) { j.t = Json::Bool; i += 5; }
        else if (s.compare(i, 4, "null") == 0) { i += 4; }
        else {
            char* end = nullptr; double v = std::strtod(s.c_str() + i, &end);
            if (end == s.c_str() + i) { ok = false; } else { j.t = Json::Num; j.n = v; i = end - s.c_str(); }
        }
        depth--;
        return j;
    }
};
inline void esc(std::string& out, const std::string& s) {
    out += '"';
    for (unsigned char c : s) {
        switch (c) {
            case '"': out += "\\\""; break; case '\\': out += "\\\\"; break; case '\n': out += "\\n"; break; case '\r': out += "\\r"; break; case '\t': out += "\\t"; break;
            default: if (c < 0x20) { char b[8]; std::snprintf(b, sizeof b, "\\u%04x", c); out += b; } else out += (char)c;
        }
    }
    out += '"';
}
}  // namespace detail

inline bool jsonParse(const std::string& text, Json& out) {
    size_t st = 0;
    if (text.size() >= 3 && (unsigned char)text[0] == 0xEF && (unsigned char)text[1] == 0xBB && (unsigned char)text[2] == 0xBF) st = 3;
    std::string body = text.substr(st);
    detail::P p(body);
    out = p.val();
    p.ws();
    return p.ok && p.i == body.size();
}

inline void jsonDump(const Json& j, std::string& out) {
    switch (j.t) {
        case Json::Null: out += "null"; break;
        case Json::Bool: out += j.b ? "true" : "false"; break;
        case Json::Num: { char b[40]; if (j.n == std::floor(j.n) && std::fabs(j.n) < 1e15) std::snprintf(b, sizeof b, "%lld", (long long)j.n); else std::snprintf(b, sizeof b, "%.10g", j.n); out += b; break; }
        case Json::Str: detail::esc(out, j.s); break;
        case Json::Arr: out += '['; for (size_t k = 0; k < j.a.size(); k++) { if (k) out += ','; jsonDump(j.a[k], out); } out += ']'; break;
        case Json::Obj: out += '{'; for (size_t k = 0; k < j.o.size(); k++) { if (k) out += ','; detail::esc(out, j.o[k].first); out += ':'; jsonDump(j.o[k].second, out); } out += '}'; break;
    }
}

}  // namespace gs
