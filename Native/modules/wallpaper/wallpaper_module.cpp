#include "wallpaper_module.hpp"
#include <shobjidl.h>
#include <shlobj.h>
#include <algorithm>
#include <cstdio>
#include <cstring>
#include <filesystem>
#include "imgui.h"
#include "json.hpp"
#include "loc.hpp"
#include "ui_common.hpp"
#include "win.hpp"

namespace fs = std::filesystem;

namespace wp {

using gs::L;
using namespace sp::ui;

static const char* kVersion = "ghoststyle-1";

// ---------- окно редактора панелей («Терминал») ----------
class EditorWindow {
public:
    EditorWindow(const Wallpaper& w, const Settings& s, const std::map<std::string, std::string>& values, const std::string& bg, std::function<void(const std::string&)> onLayout)
        : w_(w), s_(s), values_(values), onLayout_(std::move(onLayout)) {
        static bool reg = false;
        if (!reg) {
            reg = true;
            WNDCLASSEXW wc{}; wc.cbSize = sizeof wc; wc.lpfnWndProc = proc; wc.hInstance = GetModuleHandleW(nullptr); wc.lpszClassName = L"GhostStyleWpEditor";
            wc.hIcon = LoadIconW(GetModuleHandleW(nullptr), MAKEINTRESOURCEW(1)); wc.hbrBackground = (HBRUSH)GetStockObject(BLACK_BRUSH); wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
            RegisterClassExW(&wc);
        }
        hwnd_ = CreateWindowExW(0, L"GhostStyleWpEditor", L"Ghost Style — редактор панелей", WS_OVERLAPPEDWINDOW | WS_VISIBLE, CW_USEDEFAULT, CW_USEDEFAULT, 1280, 760, nullptr, nullptr, GetModuleHandleW(nullptr), this);
        SetWindowLongPtrW(hwnd_, GWLP_USERDATA, (LONG_PTR)this);
        LoadOpts lo; lo.edit = true; lo.useBackground = true; lo.background = bg;
        Wallpaper ww = w_; ww.values = values_;
        page_.open(hwnd_, ww, s_, lo, nullptr, [this](const std::string& json) {
            gs::Json j;
            if (!gs::jsonParse(json, j) || j.t != gs::Json::Obj || j.str("t") != "layout") return;
            std::string v = j.str("v");
            gs::Json chk;
            if (v.size() > 200000 || !gs::jsonParse(v, chk)) return;
            if (onLayout_) onLayout_(v);
        }, nullptr);
    }
    ~EditorWindow() { page_.web.close(); if (hwnd_) DestroyWindow(hwnd_); }
    bool open() const { return hwnd_ && IsWindow(hwnd_); }
    void focus() { if (hwnd_) { ShowWindow(hwnd_, SW_RESTORE); SetForegroundWindow(hwnd_); } }
    bool closed() const { return closed_; }
private:
    HWND hwnd_ = nullptr; Wallpaper w_; Settings s_; std::map<std::string, std::string> values_; PageHost page_; std::function<void(const std::string&)> onLayout_; bool closed_ = false;
    static LRESULT CALLBACK proc(HWND h, UINT m, WPARAM wp_, LPARAM lp) {
        auto* e = (EditorWindow*)GetWindowLongPtrW(h, GWLP_USERDATA);
        if (e && m == WM_SIZE) e->page_.web.resize(LOWORD(lp), HIWORD(lp));
        if (e && m == WM_CLOSE) { e->closed_ = true; ShowWindow(h, SW_HIDE); return 0; }
        return DefWindowProcW(h, m, wp_, lp);
    }
};

// ---------- диалоги выбора ----------
static std::wstring pick(bool folder) {
    std::wstring res;
    IFileOpenDialog* d = nullptr;
    if (FAILED(CoCreateInstance(CLSID_FileOpenDialog, nullptr, CLSCTX_ALL, IID_PPV_ARGS(&d))) || !d) return res;
    DWORD o = 0; d->GetOptions(&o);
    d->SetOptions(o | FOS_FORCEFILESYSTEM | (folder ? FOS_PICKFOLDERS : 0));
    if (!folder) {
        COMDLG_FILTERSPEC f[] = {{L"Обои (видео, GIF, картинки, страницы, архивы)", L"*.mp4;*.webm;*.m4v;*.mov;*.gif;*.png;*.jpg;*.jpeg;*.webp;*.html;*.htm;*.zip"}, {L"Все файлы", L"*.*"}};
        d->SetFileTypes(2, f);
    }
    if (SUCCEEDED(d->Show(nullptr))) {
        IShellItem* it = nullptr;
        if (SUCCEEDED(d->GetResult(&it)) && it) { PWSTR p = nullptr; if (SUCCEEDED(it->GetDisplayName(SIGDN_FILESYSPATH, &p)) && p) { res = p; CoTaskMemFree(p); } it->Release(); }
    }
    d->Release();
    return res;
}

// ---------- модуль ----------
WallpaperModule::WallpaperModule() { saved_ = Settings::load(); }
WallpaperModule::~WallpaperModule() { for (auto& t : jobs_) if (t.joinable()) t.join(); }

void WallpaperModule::start() {
    ensureBuiltins(kVersion);
    if (!saved_.Active.empty()) ctl_.apply(saved_);
}
void WallpaperModule::stop() { editor_.reset(); ctl_.stopAll(); }

// Скин: только встроенные («b-…») и каталожные («c-…») обои — свои файлы человека на другом компьютере не существуют
static bool skinWallpaperId(const std::string& id) {
    if (id.size() < 3 || id.size() > 43 || (id[0] != 'b' && id[0] != 'c') || id[1] != '-') return false;
    for (size_t i = 2; i < id.size(); i++) { char c = id[i]; if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-')) return false; }
    return true;
}
bool WallpaperModule::skinCapture(gs::Json& out) {
    if (!skinWallpaperId(saved_.Active)) return false;
    out = gs::Json::Object(); out.set("id", gs::Json::String(saved_.Active)); out.set("fit", gs::Json::String(saved_.Fit)); out.set("fps", gs::Json::Number(saved_.Fps));
    return true;
}
std::string WallpaperModule::skinApply(const gs::Json& p) {
    std::string id = p.str("id");
    if (!skinWallpaperId(id)) return "обои: нужны встроенные или каталожные";
    Wallpaper w; if (!getWallpaper(id, w)) return "этих обоев нет в библиотеке (установите их из каталога)";
    saved_.Active = id; saved_.Paused = false; saved_.Fit = p.str("fit", "cover"); saved_.Fps = p.num("fps", 60); listDirty_ = true;
    applySettings();
    return "";
}

void WallpaperModule::revert() {
    saved_.Active.clear(); saved_.save();
    ctl_.apply(saved_);
    status_ = L("Обои выключены. Системные обои Windows на месте.");
}

void WallpaperModule::refreshList() {
    list_ = listAll();
    if (list_.empty()) { ensureBuiltins(kVersion); list_ = listAll(); }   // окно открыто раньше запуска (снимки, проверки)
    listDirty_ = false;
}
const Wallpaper* WallpaperModule::find(const std::string& id) const { for (auto& w : list_) if (w.id == id) return &w; return nullptr; }

void WallpaperModule::applySettings() {
    saved_.normalize(); saved_.save();
    ctl_.apply(saved_);
    if (app_) app_->changed();
}
void WallpaperModule::activate(const std::string& id) { saved_.Active = id; saved_.Paused = false; applySettings(); }

std::string WallpaperModule::val(const Param& p) const { auto it = edit_.find(p.key); return it != edit_.end() && !it->second.empty() ? it->second : p.def; }

void WallpaperModule::startEdit(const std::string& id) {
    editId_ = id; edit_.clear(); editBg_.clear();
    const Wallpaper* w = find(id);
    if (!w) return;
    edit_ = w->values; editBg_ = w->background;
    std::string t = w->builtin() || w->tmpl ? w->title + " (мои)" : w->title;
    std::snprintf(editTitle_, sizeof editTitle_, "%s", t.c_str());
}

void WallpaperModule::importFrom(const std::wstring& path) {
    Result r = installPath(path, "из файла");
    importMsg_ = r.message;
    refreshList();
    if (r.ok) activate(r.id);
}

void WallpaperModule::openEditor() {
    const Wallpaper* w = find(editId_);
    if (!w) return;
    if (editor_ && editor_->open() && !editor_->closed()) { editor_->focus(); return; }
    editor_.reset();
    editor_ = std::make_unique<EditorWindow>(*w, saved_, edit_, editBg_, [this](const std::string& layout) { edit_["layout"] = layout; });
}

// ---------- вкладки ----------
void WallpaperModule::drawPanel() {
    if (listDirty_) refreshList();
    { std::lock_guard<std::mutex> g(mu_); for (auto& m : pendingMsgs_) catalogMsg_ = m; pendingMsgs_.clear(); }
    {   // город найден?
        std::string name, err; double la, lo;
        if (ctl_.telemetry().takeCity(name, la, lo, err)) {
            if (!err.empty()) status_ = err;
            else { saved_.WeatherName = name; saved_.WeatherLat = la; saved_.WeatherLon = lo; saved_.WeatherQuery = city_; applySettings(); status_ = std::string(L("Город найден: ")) + name; }
        }
    }
    if (editor_ && editor_->closed()) { editor_.reset(); }
    ImGui::PushFont(mono());
    ImGui::PushStyleColor(ImGuiCol_Text, rgb(0, 240, 255));
    ImGui::TextUnformatted(L("Обои"));
    ImGui::PopStyleColor(); ImGui::PopFont();
    note("Живые обои рабочего стола: видео, GIF, картинки, страницы и «Терминал». Системные обои Windows не меняются: «Выключить» возвращает рабочий стол как был.");
    ImGui::Spacing();
    if (ImGui::BeginTabBar("wptabs")) {
        if (ImGui::BeginTabItem(L("Мои обои"))) { tabLibrary(); ImGui::EndTabItem(); }
        if (ImGui::BeginTabItem(L("Каталог"))) { tabCatalog(); ImGui::EndTabItem(); }
        if (ImGui::BeginTabItem(L("Создать"))) { tabCreate(); ImGui::EndTabItem(); }
        if (ImGui::BeginTabItem(L("Настройки"))) { tabSettings(); ImGui::EndTabItem(); }
        ImGui::EndTabBar();
    }
}

void WallpaperModule::tabLibrary() {
    ImGui::Spacing();
    ImGui::PushStyleColor(ImGuiCol_Text, muted());
    ImGui::TextWrapped("%s", ctl_.status().empty() ? L(active() ? "Обои включены." : "Обои выключены.") : L(ctl_.status().c_str()));
    ImGui::PopStyleColor();
    if (!importMsg_.empty()) { ImGui::PushStyleColor(ImGuiCol_Text, muted()); ImGui::TextWrapped("%s", L(importMsg_.c_str())); ImGui::PopStyleColor(); }
    if (!status_.empty()) { ImGui::PushStyleColor(ImGuiCol_Text, muted()); ImGui::TextWrapped("%s", L(status_.c_str())); ImGui::PopStyleColor(); }
    if (!sp::WebView::available()) { ImGui::PushStyleColor(ImGuiCol_Text, rgb(255, 176, 32)); ImGui::TextWrapped("%s", L("Не найден «Microsoft Edge WebView2 Runtime» — без него обои не показываются. В Windows 11 он есть; для Windows 10 скачайте его с сайта Microsoft.")); ImGui::PopStyleColor(); }
    ImGui::Spacing();
    if (ImGui::Button(L("Добавить файл…"))) { auto p = pick(false); if (!p.empty()) importFrom(p); }
    ImGui::SameLine();
    if (ImGui::Button(L("Добавить папку…"))) { auto p = pick(true); if (!p.empty()) importFrom(p); }
    ImGui::SameLine();
    if (ImGui::Button(L(saved_.Paused ? "Продолжить" : "Пауза"))) { saved_.Paused = !saved_.Paused; applySettings(); }
    ImGui::SameLine();
    if (ImGui::Button(L("Выключить обои"))) revert();
    ImGui::Spacing();
    for (size_t i = 0; i < list_.size(); i++) {
        const Wallpaper& w = list_[i];
        ImGui::PushID((int)i);
        if (ImGui::BeginChild(w.id.c_str(), ImVec2(0, 0), ImGuiChildFlags_AutoResizeY | ImGuiChildFlags_Borders | ImGuiChildFlags_AlwaysUseWindowPadding)) {
            bool on = saved_.Active == w.id;
            ImGui::PushFont(mono()); ImGui::TextUnformatted(w.title.c_str()); ImGui::PopFont();
            ImGui::SameLine(); ImGui::PushStyleColor(ImGuiCol_Text, muted());
            char info[160]; std::snprintf(info, sizeof info, "  %s · %s · %.1f %s%s", L(w.typeLabel()), L(w.builtin() ? "встроенные" : w.source.empty() ? "мои" : w.source.c_str()), w.sizeBytes / 1048576.0, L("МБ"), w.overlay ? L(" · слой") : "");
            ImGui::TextUnformatted(info); ImGui::PopStyleColor();
            if (!w.description.empty() && w.type == "html") { ImGui::PushStyleColor(ImGuiCol_Text, muted()); ImGui::TextWrapped("%s", L(w.description.c_str())); ImGui::PopStyleColor(); }
            if (on) { ImGui::PushStyleColor(ImGuiCol_Button, rgb(0, 240, 255)); ImGui::PushStyleColor(ImGuiCol_Text, rgb(6, 5, 13)); }
            if (ImGui::Button(L(on ? "Включено ✓" : "Включить"))) activate(w.id);
            if (on) ImGui::PopStyleColor(2);
            if (!w.params.empty()) { ImGui::SameLine(); if (ImGui::Button(L("Настроить"))) { startEdit(w.id); } }
            if (!w.builtin()) {
                ImGui::SameLine();
                bool online = w.online;
                if (ImGui::Checkbox(L("Разрешить интернет"), &online)) { Wallpaper c = w; c.online = online; saveWallpaper(c); listDirty_ = true; if (on) applySettings(); }
                ImGui::SameLine();
                if (confirmDelete_ == w.id) {
                    if (ImGui::Button(L("Точно удалить?"))) { if (on) { saved_.Active.clear(); applySettings(); } removeWallpaper(w.id); confirmDelete_.clear(); listDirty_ = true; }
                } else if (ImGui::Button(L("Удалить"))) confirmDelete_ = w.id;
            }
        }
        ImGui::EndChild(); ImGui::Spacing();
        ImGui::PopID();
    }
}

void WallpaperModule::tabCatalog() {
    ImGui::Spacing();
    note("Список лежит в репозитории проекта: новые обои появляются здесь без нового выпуска программы. Скачиваются только страницы, картинки, видео и шрифты — программ среди них нет.");
    if (ImGui::Button(L(catalogBusy_ ? "Загружаю…" : "Обновить список")) && !catalogBusy_) {
        catalogBusy_ = true; catalogErr_.clear();
        jobs_.emplace_back([this] {
            std::vector<CatalogItem> items; std::string err;
            bool ok = fetchCatalog(items, err);
            std::lock_guard<std::mutex> g(mu_);
            if (ok) { catalog_ = items; catalogLoaded_ = true; } else catalogErr_ = "Не получилось загрузить каталог: " + err;
            catalogBusy_ = false;
        });
    }
    if (!catalogErr_.empty()) { ImGui::PushStyleColor(ImGuiCol_Text, rgb(255, 90, 90)); ImGui::TextWrapped("%s", catalogErr_.c_str()); ImGui::PopStyleColor(); }
    if (!catalogMsg_.empty()) { ImGui::PushStyleColor(ImGuiCol_Text, muted()); ImGui::TextWrapped("%s", L(catalogMsg_.c_str())); ImGui::PopStyleColor(); }
    std::vector<CatalogItem> items; { std::lock_guard<std::mutex> g(mu_); items = catalog_; }
    for (size_t i = 0; i < items.size(); i++) {
        auto& c = items[i];
        ImGui::PushID((int)i);
        if (ImGui::BeginChild(("cat-" + c.id).c_str(), ImVec2(0, 0), ImGuiChildFlags_AutoResizeY | ImGuiChildFlags_Borders | ImGuiChildFlags_AlwaysUseWindowPadding)) {
            ImGui::PushFont(mono()); ImGui::TextUnformatted(c.title.c_str()); ImGui::PopFont();
            ImGui::PushStyleColor(ImGuiCol_Text, muted()); ImGui::TextWrapped("%s", c.description.c_str()); ImGui::PopStyleColor();
            Wallpaper tmp; bool have = getWallpaper(c.libraryId(), tmp);
            if (have) ImGui::TextUnformatted(L("Установлено"));
            else if (ImGui::Button(L("Установить")) && !catalogBusy_) {
                catalogBusy_ = true; CatalogItem copy = c;
                jobs_.emplace_back([this, copy] {
                    Result r = installCatalogItem(copy);
                    std::lock_guard<std::mutex> g(mu_);
                    pendingMsgs_.push_back(r.message); listDirty_ = true; catalogBusy_ = false;
                });
            }
        }
        ImGui::EndChild(); ImGui::Spacing();
        ImGui::PopID();
    }
    if (!catalogLoaded_ && !catalogBusy_ && catalogErr_.empty()) { ImGui::PushStyleColor(ImGuiCol_Text, muted()); ImGui::TextUnformatted(L("Нажмите «Обновить список».")); ImGui::PopStyleColor(); }
}

void WallpaperModule::tabCreate() {
    ImGui::Spacing();
    note("Выберите основу, настройте цвета и ползунки и нажмите «Сохранить». Свои обои сохраняются в «Моих обоях»; встроенные при этом не меняются.");
    // выбор основы
    const Wallpaper* cur = find(editId_);
    ImGui::SetNextItemWidth(360);
    if (ImGui::BeginCombo("##base", cur ? cur->title.c_str() : L("— выберите обои —"))) {
        for (auto& w : list_) if (!w.params.empty()) { if (ImGui::Selectable(w.title.c_str(), w.id == editId_)) startEdit(w.id); }
        ImGui::EndCombo();
    }
    cur = find(editId_);
    if (!cur) return;
    ImGui::Spacing();
    bool changed = false;
    for (auto& p : cur->params) {
        ImGui::PushID(p.key.c_str());
        if (p.type == "layout") { ImGui::PopID(); continue; }
        rowTitle(L(p.label.c_str()), "");
        std::string v = val(p);
        if (p.type == "color") {
            unsigned r = 255, g = 255, b = 255; std::sscanf(v.c_str(), "#%02x%02x%02x", &r, &g, &b);
            float c3[3] = {r / 255.f, g / 255.f, b / 255.f};
            if (ImGui::ColorEdit3("##c", c3, ImGuiColorEditFlags_NoInputs | ImGuiColorEditFlags_PickerHueBar)) {
                char hx[16]; std::snprintf(hx, sizeof hx, "#%02X%02X%02X", (int)std::lround(c3[0] * 255), (int)std::lround(c3[1] * 255), (int)std::lround(c3[2] * 255));
                edit_[p.key] = hx; changed = true;
            }
            ImGui::SameLine(); ImGui::TextUnformatted(v.c_str());
        } else if (p.type == "choice") {
            int sel = 0; for (size_t i = 0; i < p.options.size(); i++) if (p.options[i] == v) sel = (int)i;
            ImGui::SetNextItemWidth(260);
            if (ImGui::BeginCombo("##ch", p.options.empty() ? "" : p.options[sel].c_str())) {
                for (size_t i = 0; i < p.options.size(); i++) if (ImGui::Selectable(p.options[i].c_str(), (int)i == sel)) { edit_[p.key] = p.options[i]; changed = true; }
                ImGui::EndCombo();
            }
        } else if (p.type == "text") {
            char buf[256]; std::snprintf(buf, sizeof buf, "%s", v.c_str());
            ImGui::SetNextItemWidth(360);
            if (ImGui::InputText("##t", buf, sizeof buf)) { edit_[p.key] = buf; changed = true; }
        } else {
            double x = std::atof(v.c_str()); float f = (float)x;
            bool intStep = p.step >= 1 && p.step == std::floor(p.step);
            ImGui::SetNextItemWidth(360);
            if (ImGui::SliderFloat("##r", &f, (float)p.min, (float)p.max, intStep ? "%.0f" : "%.2f")) {
                double q = p.step > 0 ? std::round((f - p.min) / p.step) * p.step + p.min : f;
                char b[40]; if (intStep) std::snprintf(b, sizeof b, "%.0f", q); else std::snprintf(b, sizeof b, "%g", std::round(q * 1000) / 1000);
                edit_[p.key] = b; changed = true;
            }
        }
        ImGui::PopID();
        ImGui::Spacing();
    }
    if (cur->overlay) {
        rowTitle(L("Что под терминалом"), "прозрачный слой лежит поверх других обоев; если ничего не выбрано — чёрный экран");
        const Wallpaper* bg = find(editBg_);
        ImGui::SetNextItemWidth(360);
        if (ImGui::BeginCombo("##bg", bg ? bg->title.c_str() : L("— ничего —"))) {
            if (ImGui::Selectable(L("— ничего —"), editBg_.empty())) { editBg_.clear(); changed = true; }
            for (auto& w : list_) if (!w.overlay && w.id != cur->id) if (ImGui::Selectable(w.title.c_str(), w.id == editBg_)) { editBg_ = w.id; changed = true; }
            ImGui::EndCombo();
        }
        ImGui::Spacing();
        if (ImGui::Button(L("Открыть редактор панелей…"))) openEditor();
        ImGui::SameLine(); ImGui::PushStyleColor(ImGuiCol_Text, muted()); ImGui::TextUnformatted(L("двигайте панели мышью, добавляйте свои")); ImGui::PopStyleColor();
        ImGui::Spacing();
    }
    bool own = !cur->builtin() && !cur->tmpl;
    ImGui::SetNextItemWidth(360);
    ImGui::InputText(L("Название"), editTitle_, sizeof editTitle_);
    ImGui::Spacing();
    ImGui::PushStyleColor(ImGuiCol_Button, rgb(0, 240, 255)); ImGui::PushStyleColor(ImGuiCol_Text, rgb(6, 5, 13));
    if (ImGui::Button(L(own ? "Сохранить и показать" : "Сохранить как мои обои и включить"))) {
        if (own) { Wallpaper w = *cur; updateValues(w, edit_, editBg_); if (editTitle_[0]) { w.title = editTitle_; saveWallpaper(w); } listDirty_ = true; activate(w.id); status_ = L("Сохранено."); }
        else { Result r = createFrom(*cur, editTitle_, edit_, editBg_); status_ = r.message; listDirty_ = true; if (r.ok) { refreshList(); activate(r.id); startEdit(r.id); } }
    }
    ImGui::PopStyleColor(2);
    // живой показ изменений для своих обоев, которые сейчас на рабочем столе
    if (changed && own && saved_.Active == cur->id) liveAt_ = ImGui::GetTime() + 0.35;
    if (liveAt_ > 0 && ImGui::GetTime() >= liveAt_) { liveAt_ = 0; Wallpaper w = *cur; updateValues(w, edit_, editBg_); listDirty_ = true; ctl_.apply(saved_); }
    if (!status_.empty()) { ImGui::PushStyleColor(ImGuiCol_Text, muted()); ImGui::TextWrapped("%s", L(status_.c_str())); ImGui::PopStyleColor(); }
}

void WallpaperModule::tabSettings() {
    ImGui::Spacing();
    bool ch = false;
    ch |= toggle("Все мониторы", "одни и те же обои на каждом мониторе (выкл. — только на главном)", saved_.AllMonitors);
    ch |= toggle("Пауза в играх и видео", "когда идёт полноэкранная игра, видео или презентация, обои замирают и не грузят видеокарту", saved_.PauseOnFullscreen);
    ch |= toggle("Пауза, когда окно закрыло рабочий стол", "обои не видны — зачем их рисовать", saved_.PauseWhenCovered);
    ch |= toggle("Выключить звук обоев", "у видео и страниц со звуком", saved_.Mute);
    int fit = saved_.Fit == "contain" ? 1 : saved_.Fit == "fill" ? 2 : 0;
    static const char* fits[] = {"Заполнить", "Вписать", "Растянуть"};
    rowTitle("Как вписывать картинку и видео", "заполнить — без полос, вписать — целиком, растянуть — на весь экран");
    if (seg("fit", fits, 3, fit)) { saved_.Fit = fit == 1 ? "contain" : fit == 2 ? "fill" : "cover"; ch = true; }
    ImGui::Spacing();
    ch |= slider("Кадров в секунду", "у встроенных обоев-страниц (меньше — легче для компьютера)", saved_.Fps, 15, 60);
    if (ch) applySettings();
    ImGui::Separator();
    ImGui::PushFont(mono()); ImGui::TextUnformatted(L("«Терминал»: данные компьютера")); ImGui::PopFont();
    note("Страница обоев ничего не просит сама: данные только приходят к ней сообщениями и только если они перечислены в её описании. Ничего не записывается и не отправляется — кроме запроса погоды к Open-Meteo по названию города.");
    bool c2 = false;
    c2 |= toggle("Звук и что играет", "уровень и спектр звука системы (сам звук не записывается)", saved_.TelAudio);
    c2 |= toggle("Журнал событий", "какие программы открыты и закрыты, нагрузка, громкость", saved_.TelJournal);
    if (c2) applySettings();
    rowTitle("Город для погоды", saved_.WeatherName.empty() ? "не выбран" : saved_.WeatherName.c_str());
    ImGui::SetNextItemWidth(260);
    if (city_[0] == 0 && !saved_.WeatherQuery.empty()) std::snprintf(city_, sizeof city_, "%s", saved_.WeatherQuery.c_str());
    ImGui::InputText("##city", city_, sizeof city_);
    ImGui::SameLine();
    if (ImGui::Button(L(ctl_.telemetry().cityBusy() ? "Ищу…" : "Найти"))) ctl_.telemetry().findCity(city_);
    ImGui::SameLine();
    if (ImGui::Button(L("Убрать город"))) { saved_.WeatherName.clear(); saved_.WeatherQuery.clear(); city_[0] = 0; applySettings(); }
    if (!status_.empty()) { ImGui::PushStyleColor(ImGuiCol_Text, muted()); ImGui::TextWrapped("%s", L(status_.c_str())); ImGui::PopStyleColor(); }
}

}  // namespace wp
