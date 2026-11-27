#include "icons_module.hpp"
#include <windows.h>
#include <shellapi.h>
#include <shlobj.h>
#include <shobjidl.h>
#include <d3d11.h>
#include <algorithm>
#include <cstdio>
#include <filesystem>
#include <set>
#include "cursor_settings.hpp"
#include "imgui.h"
#include "loc.hpp"
#include "ui_common.hpp"
#include "win.hpp"

namespace fs = std::filesystem;

namespace ic {

using gs::L;
using namespace sp::ui;

IconsModule::IconsModule() { draft_ = Settings::load(); }
IconsModule::~IconsModule() { if (job_.joinable()) job_.join(); if (previewJob_.joinable()) previewJob_.join(); }

bool IconsModule::skinCapture(gs::Json& o) {
    if (!engine_.applied()) return false;
    Settings s = Settings::load(); o = gs::Json::Object();
    o.set("color", gs::Json::String(s.Color)); o.set("style", gs::Json::String(s.Style)); o.set("strength", gs::Json::Number(s.Strength));
    return true;
}
std::string IconsModule::skinApply(const gs::Json& p) {
    if (busy_) return "значки сейчас обновляются — повторите чуть позже";
    draft_.Color = p.str("color", draft_.Color); draft_.Style = p.str("style", draft_.Style); draft_.Strength = p.num("strength", draft_.Strength); draft_.normalize();
    startApply();
    return "";
}

void IconsModule::revert() { if (busy_) return; engine_.restoreAll(true); status_ = L("Все значки возвращены как были."); haveLast_ = false; }

Microsoft::WRL::ComPtr<ID3D11ShaderResourceView> IconsModule::makeTex(const Img& im) {
    Microsoft::WRL::ComPtr<ID3D11ShaderResourceView> srv;
    if (!dev_ || im.w <= 0 || im.bgra.size() < (size_t)im.w * im.h * 4) return srv;
    D3D11_TEXTURE2D_DESC d{}; d.Width = im.w; d.Height = im.h; d.MipLevels = 1; d.ArraySize = 1; d.Format = DXGI_FORMAT_B8G8R8A8_UNORM; d.SampleDesc.Count = 1; d.Usage = D3D11_USAGE_IMMUTABLE; d.BindFlags = D3D11_BIND_SHADER_RESOURCE;
    D3D11_SUBRESOURCE_DATA sd{im.bgra.data(), (UINT)(im.w * 4), 0};
    Microsoft::WRL::ComPtr<ID3D11Texture2D> tex;
    if (SUCCEEDED(dev_->CreateTexture2D(&d, &sd, &tex))) dev_->CreateShaderResourceView(tex.Get(), nullptr, &srv);
    return srv;
}

void IconsModule::startApply() {
    if (busy_) return;
    busy_ = true; if (job_.joinable()) job_.join();
    Settings s = draft_; s.normalize(); s.save();
    job_ = std::thread([this, s] {
        CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
        ApplyResult r = engine_.applyColor(s, [this](const std::string& t) { std::lock_guard<std::mutex> g(mu_); progress_ = t; });
        { std::lock_guard<std::mutex> g(mu_); last_ = r; haveLast_ = true; progress_.clear(); status_ = "Готово: перекрашено " + std::to_string(r.done) + ", не вышло " + std::to_string(r.failed) + "."; }
        busy_ = false;
        CoUninitialize();
    });
}
void IconsModule::startRevert() {
    if (busy_) return;
    busy_ = true; if (job_.joinable()) job_.join();
    job_ = std::thread([this] { CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED); engine_.restoreAll(true); { std::lock_guard<std::mutex> g(mu_); status_ = "Все значки возвращены как были."; haveLast_ = false; } busy_ = false; CoUninitialize(); });
}

void IconsModule::startPreview() {
    if (previewBusy_) return;
    previewBusy_ = true; if (previewJob_.joinable()) previewJob_.join();
    Settings s = draft_;
    previewJob_ = std::thread([this, s] {
        CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
        auto p = engine_.previews(s);
        { std::lock_guard<std::mutex> g(mu_); previews_ = p; previewsFresh_ = true; }
        previewBusy_ = false;
        CoUninitialize();
    });
}

void IconsModule::drawPreview() {
    // превью: пересчитываем через 0,5 с после смены цвета
    std::string key = draft_.Color + "|" + draft_.Style + "|" + std::to_string(draft_.Strength);
    double now = ImGui::GetTime();
    if (key != previewKey_) { previewKey_ = key; previewAt_ = now + 0.5; }
    if (previewAt_ > 0 && now >= previewAt_ && !busy_) { previewAt_ = 0; startPreview(); }
    {
        std::lock_guard<std::mutex> g(mu_);
        if (previewsFresh_ && dev_) { texBefore_.clear(); texAfter_.clear(); for (auto& p : previews_) { texBefore_.push_back(p.hasBefore ? makeTex(p.before) : nullptr); texAfter_.push_back(p.hasAfter ? makeTex(p.after) : nullptr); } previewsFresh_ = false; }
    }
    {
        std::vector<Preview> pv; { std::lock_guard<std::mutex> g(mu_); pv = previews_; }
        if (pv.empty()) { ImGui::PushStyleColor(ImGuiCol_Text, muted()); ImGui::TextUnformatted(L(previewBusy_ ? "Загружаю значки…" : "Предпросмотр появится через секунду.")); ImGui::PopStyleColor(); }
        for (size_t i = 0; i < pv.size() && i < texBefore_.size(); i++) {
            ImGui::BeginGroup();
            if (texBefore_[i]) ImGui::Image((ImTextureID)(intptr_t)texBefore_[i].Get(), ImVec2(40, 40)); else ImGui::Dummy(ImVec2(40, 40));
            ImGui::SameLine(); ImGui::TextUnformatted("→"); ImGui::SameLine();
            if (texAfter_[i]) ImGui::Image((ImTextureID)(intptr_t)texAfter_[i].Get(), ImVec2(40, 40)); else ImGui::Dummy(ImVec2(40, 40));
            ImGui::PushStyleColor(ImGuiCol_Text, muted()); ImGui::TextWrapped("%s", L(pv[i].problem.empty() ? pv[i].name.c_str() : pv[i].problem.c_str())); ImGui::PopStyleColor();
            ImGui::EndGroup();
            if ((i + 1) % 4 != 0 && i + 1 < pv.size()) { ImGui::SameLine(0, 24); }
        }
    }
}

void IconsModule::drawPanel() {
    ImGui::PushFont(mono()); ImGui::PushStyleColor(ImGuiCol_Text, rgb(200, 208, 218)); ImGui::TextUnformatted(L("Значки")); ImGui::PopStyleColor(); ImGui::PopFont();
    note("Один цвет для значков Windows: диски, «Этот компьютер», корзина, типы файлов, папки пользователя и ярлыки. Форма и свет значков сохраняются, меняется цвет. Всё запоминается — «Вернуть как было» возвращает как было.");
    ImGui::PushStyleColor(ImGuiCol_Button, rgb(255, 42, 157));
    if (ImGui::Button(L(busy_ ? "Работаю…" : "Вернуть обычные значки Windows"))) startRevert();
    ImGui::PopStyleColor();
    ImGui::Spacing();
    if (beginCard("1. Выберите цвет")) {
        sp::Rgb c = sp::parseHex(draft_.Color);
        float col[3] = {c.r / 255.f, c.g / 255.f, c.b / 255.f};
        if (ImGui::ColorPicker3("##c", col, ImGuiColorEditFlags_NoSidePreview | ImGuiColorEditFlags_NoInputs | ImGuiColorEditFlags_PickerHueWheel)) draft_.Color = sp::toHex({col[0] * 255, col[1] * 255, col[2] * 255});
        static const char* sw[] = {"#FF2A9D", "#FF4D4D", "#FF8A3D", "#FFB020", "#F5D90A", "#20C27A", "#00F0FF", "#1E6BFF", "#6C4DFF", "#C04DFF", "#F2F2F2", "#101018"};
        for (int i = 0; i < 12; i++) { if (i) ImGui::SameLine(); ImGui::PushID(i); sp::Rgb s = sp::parseHex(sw[i]); if (ImGui::ColorButton("##s", ImVec4(s.r / 255.f, s.g / 255.f, s.b / 255.f, 1), ImGuiColorEditFlags_NoTooltip, ImVec2(28, 28))) draft_.Color = sw[i]; ImGui::PopID(); }
        ImGui::Spacing();
        int st = draft_.Style == "hue" ? 1 : 0; static const char* styles[] = {"Один цвет", "Сменить оттенок"};
        rowTitle("Способ", "один цвет — все значки в оттенках выбранного; сменить оттенок — краски значка остаются, сдвигается оттенок");
        if (seg("style", styles, 2, st)) draft_.Style = st == 1 ? "hue" : "mono";
        slider("Сила", "сколько от нового цвета (остальное — исходный значок)", draft_.Strength, 0, 100);
    }
    endCard();
    if (beginCard("2. Что перекрашивать")) {
        std::string group;
        for (auto& t : targets()) {
            if (group != t.group) { group = t.group; ImGui::PushStyleColor(ImGuiCol_Text, rgb(200, 208, 218)); ImGui::TextUnformatted(L(t.group)); ImGui::PopStyleColor(); }
            bool on = std::find(draft_.Enabled.begin(), draft_.Enabled.end(), t.id) != draft_.Enabled.end();
            ImGui::PushID(t.id);
            if (ImGui::Checkbox(L(t.title), &on)) { if (on) draft_.Enabled.push_back(t.id); else draft_.Enabled.erase(std::find(draft_.Enabled.begin(), draft_.Enabled.end(), std::string(t.id))); }
            ImGui::PopID();
        }
    }
    endCard();
    bool ar = draft_.AutoRestart;
    if (toggle("Перезапускать Проводник сам", "после применения — чтобы значки сразу везде были новыми (панель задач и окна на пару секунд пропадут)", ar)) draft_.AutoRestart = ar;
    ImGui::PushStyleColor(ImGuiCol_Button, rgb(200, 208, 218)); ImGui::PushStyleColor(ImGuiCol_Text, rgb(6, 5, 13));
    if (ImGui::Button(L(busy_ ? "Работаю…" : "Применить цвет"))) startApply();
    ImGui::PopStyleColor(2);
    ImGui::SameLine();
    std::string st, pr; ApplyResult last; bool have;
    { std::lock_guard<std::mutex> g(mu_); st = status_; pr = progress_; last = last_; have = haveLast_; }
    ImGui::PushStyleColor(ImGuiCol_Text, muted());
    if (!pr.empty()) ImGui::TextWrapped("%s %s", L("Работаю:"), L(pr.c_str()));
    else if (!st.empty()) ImGui::TextWrapped("%s", L(st.c_str()));
    else ImGui::TextWrapped("%s", L(engine_.applied() ? "Применено." : "Не применено."));
    ImGui::PopStyleColor();
    if (have && last.failed > 0 && ImGui::CollapsingHeader(L("Что не перекрасилось и почему"))) {
        int n = 0;
        for (auto& o : last.outcomes) { if (o.ok()) continue; if (n++ >= 60) { ImGui::TextUnformatted("…"); break; } ImGui::PushStyleColor(ImGuiCol_Text, muted()); ImGui::TextWrapped("%s — %s", o.item.c_str(), L(o.result.c_str())); ImGui::PopStyleColor(); }
    }
}

// ---------- проверка ----------
int IconsModule::selfTest(const std::wstring& dir, std::string& log) {
    int fails = 0;
    auto check = [&](const std::string& name, bool ok, const std::string& extra = "") { log += (ok ? "PASS " : "FAIL ") + name + (extra.empty() ? "" : " — " + extra) + "\n"; if (!ok) fails++; };
    // чистая логика
    Img a; a.w = a.h = 4; a.bgra.assign(64, 0);
    for (int i = 0; i < 16; i++) { a.bgra[i * 4] = 200; a.bgra[i * 4 + 1] = 50; a.bgra[i * 4 + 2] = 40; a.bgra[i * 4 + 3] = (i < 8 ? 255 : 128); }
    Img m = a; tintColor(m, "#00FF00", "mono", 100);
    check("перекраска: прозрачность не меняется", m.bgra[3] == 255 && m.bgra[4 * 12 + 3] == 128);
    check("перекраска: зелёного стало больше всего", m.bgra[1] > m.bgra[0] && m.bgra[1] > m.bgra[2], std::to_string(m.bgra[0]) + "," + std::to_string(m.bgra[1]) + "," + std::to_string(m.bgra[2]));
    Img h = a; tintColor(h, "#00FF00", "hue", 0);
    check("сила 0 — значок как был", h.bgra == a.bgra);
    Img flat; flat.w = flat.h = 64; flat.bgra.assign(64 * 64 * 4, 255); check("сплошная плитка — «пустой» значок", isBlank(flat));
    Img art = flat; for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) if ((x / 8 + y / 8) % 2) { art.bgra[((size_t)y * 64 + x) * 4] = 10; art.bgra[((size_t)y * 64 + x) * 4 + 1] = 200; }
    check("рисунок — не пустой", !isBlank(art));
    std::string ico = buildIco({resizeImg(art, 32), resizeImg(art, 16)});
    check("файл .ico собирается (2 размера, PNG внутри)", ico.size() > 100 && ico[2] == 1 && ico[4] == 2 && ico.find("PNG") != std::string::npos);

    // настоящая Windows: применение и возврат
    Settings orig = Settings::load();
    fs::path desk = fs::path(L"");
    wchar_t dp[MAX_PATH]; SHGetFolderPathW(nullptr, CSIDL_DESKTOPDIRECTORY, nullptr, 0, dp); desk = dp;
    std::wstring lnkFile = (desk / L"GhostStyleIconTest.lnk").wstring();
    {
        CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
        Microsoft::WRL::ComPtr<IShellLinkW> sl; Microsoft::WRL::ComPtr<IPersistFile> pf;
        wchar_t sys[MAX_PATH]; GetSystemDirectoryW(sys, MAX_PATH);
        if (SUCCEEDED(CoCreateInstance(CLSID_ShellLink, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&sl)))) { sl->SetPath((std::wstring(sys) + L"\\notepad.exe").c_str()); sl.As(&pf); pf->Save(lnkFile.c_str(), TRUE); }
    }
    check("тестовый ярлык создан", fs::exists(lnkFile));
    auto readLnkIcon = [&]() -> std::string {
        Microsoft::WRL::ComPtr<IShellLinkW> sl; Microsoft::WRL::ComPtr<IPersistFile> pf; wchar_t b[MAX_PATH * 2] = {}; int idx = 0;
        if (SUCCEEDED(CoCreateInstance(CLSID_ShellLink, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&sl))) && SUCCEEDED(sl.As(&pf)) && SUCCEEDED(pf->Load(lnkFile.c_str(), STGM_READ))) sl->GetIconLocation(b, (int)std::size(b), &idx);
        return gs::narrow(b);
    };
    auto regIcon = [](const char* path) { HKEY k; wchar_t b[1024] = {}; DWORD sz = sizeof b; std::string r; if (RegOpenKeyExW(HKEY_CURRENT_USER, gs::widen(path).c_str(), 0, KEY_QUERY_VALUE, &k) == ERROR_SUCCESS) { if (RegQueryValueExW(k, L"", nullptr, nullptr, (BYTE*)b, &sz) == ERROR_SUCCESS) r = gs::narrow(b); RegCloseKey(k); } return r; };
    const char* pcKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\CLSID\\{20D04FE0-3AEA-1069-A2D8-08002B30309D}\\DefaultIcon";
    std::string pcBefore = regIcon(pcKey), lnkBefore = readLnkIcon();
    Settings t; t.Color = "#20C27A"; t.Style = "mono"; t.Strength = 100; t.AutoRestart = false;
    t.Enabled = {"thispc", "recycle-empty", "drive-fixed", "ft-text", "kf", "lnk-desktop"};
    ApplyResult r = engine_.applyColor(t, nullptr, false);
    for (auto& o : r.outcomes) log += "  " + o.group + " | " + o.item + " | " + o.result + "\n";
    check("применение: что-то перекрашено", r.done >= 4, "перекрашено " + std::to_string(r.done) + ", не вышло " + std::to_string(r.failed));
    std::string pcAfter = regIcon(pcKey);
    check("«Этот компьютер»: в реестре наш значок", pcAfter.find("generated") != std::string::npos, pcAfter);
    std::string lnkAfter = readLnkIcon();
    check("ярлык на рабочем столе получил наш значок", lnkAfter.find("generated") != std::string::npos, lnkAfter);
    bool icoOk = false, greener = false;
    if (lnkAfter.find("generated") != std::string::npos) {
        std::vector<Img> sz; std::string ipath = lnkAfter.substr(0, lnkAfter.find_last_of(','));
        std::string data; icoOk = gs::readFile(gs::widen(ipath), data) && data.size() > 200 && data[2] == 1;
        // читаем первый размер как картинку: по среднему цвету видно, что стало зелёным
        extern bool icLoadForTest(const std::string&, std::vector<Img>&);
        if (icLoadForTest(ipath, sz) && !sz.empty()) { double g = 0, rr = 0, n = 0; for (size_t i = 0; i < sz[0].bgra.size(); i += 4) if (sz[0].bgra[i + 3] > 200) { rr += sz[0].bgra[i + 2]; g += sz[0].bgra[i + 1]; n++; } greener = n > 0 && g / n > rr / n; }
    }
    check("файл значка — настоящий .ico", icoOk);
    check("в файле значка преобладает зелёный", greener);
    // права администратора: значок папок и ярлыки «для всех» (на раннере администратор)
    if (isAdmin()) {
        Settings fa = t; fa.Enabled = {"folder"};
        ApplyResult fr = engine_.applyColor(fa, nullptr, false);
        for (auto& o : fr.outcomes) log += "  " + o.group + " | " + o.item + " | " + o.result + "\n";
        HKEY k; wchar_t b[1024] = {}; DWORD sz = sizeof b; bool has = false;
        if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Shell Icons", 0, KEY_QUERY_VALUE, &k) == ERROR_SUCCESS) { has = RegQueryValueExW(k, L"3", nullptr, nullptr, (BYTE*)b, &sz) == ERROR_SUCCESS; RegCloseKey(k); }
        check("значок обычных папок записан (администратор)", has && std::wstring(b).find(L"generated") != std::wstring::npos, gs::narrow(b));
    } else log += "ПРОПУСК: проверка действий администратора (нет прав)\n";
    int restored = engine_.restoreAll(false);
    check("«Вернуть как было»: что-то возвращено", restored > 0, std::to_string(restored));
    check("«Этот компьютер»: значок как был", regIcon(pcKey) == pcBefore, regIcon(pcKey) + " ≟ " + pcBefore);
    check("ярлык: значок как был", readLnkIcon() == lnkBefore, readLnkIcon() + " ≟ " + lnkBefore);
    HKEY k2; bool leftover = false;
    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Shell Icons", 0, KEY_QUERY_VALUE, &k2) == ERROR_SUCCESS) { wchar_t bb[512]; DWORD s2 = sizeof bb; leftover = RegQueryValueExW(k2, L"3", nullptr, nullptr, (BYTE*)bb, &s2) == ERROR_SUCCESS && std::wstring(bb).find(L"generated") != std::wstring::npos; RegCloseKey(k2); }
    check("значок папок в HKLM возвращён", !leftover);
    DeleteFileW(lnkFile.c_str());
    orig.save();
    (void)dir;
    return fails;
}

}  // namespace ic
