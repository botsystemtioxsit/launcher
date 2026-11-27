#include "taskbar_module.hpp"
#include <algorithm>
#include <cstdio>
#include "imgui.h"
#include "loc.hpp"
#include "tb_registry.hpp"
#include "cursor_settings.hpp"
#include "tb_settings.hpp"
#include "ui_common.hpp"
#include "win.hpp"

namespace tb {

using gs::L;
using namespace sp::ui;

int runLogicChecks(std::string& log);

TaskbarModule::TaskbarModule() : overlay_(&engine_) {
    saved_ = loadAppearance(); draft_ = saved_; shapeSel_ = selFrom(draft_);
    ColorLink l = colorLinkCurrent(); cOn_ = l.active && l.mode == "solid"; cColor_ = l.color; cOp_ = l.opacity;
}
TaskbarModule::~TaskbarModule() { if (job_.joinable()) job_.join(); if (timerWin_) { KillTimer(timerWin_, 1); DestroyWindow(timerWin_); } }

int TaskbarModule::selFrom(const Appearance& a) const {
    if (a.Shape == "pill") return 1;
    if (a.Shape == "islands") return a.IslandStyle == "tab" ? 4 : 2;
    if (a.Shape == "tab") return 3;
    return 0;
}
void TaskbarModule::presetFrom(int sel) {
    switch (sel) {
        case 0: draft_.Shape = "none"; break;
        case 1: draft_.Shape = "pill"; break;
        case 2: draft_.Shape = "islands"; draft_.IslandStyle = "float"; break;
        case 3: draft_.Shape = "tab"; break;
        case 4: draft_.Shape = "islands"; draft_.IslandStyle = "tab"; break;
    }
}

LRESULT CALLBACK TaskbarModule::timerProc(HWND h, UINT m, WPARAM w, LPARAM l) {
    if (m == WM_TIMER) { auto* t = (TaskbarModule*)GetWindowLongPtrW(h, GWLP_USERDATA); if (t) t->overlay_.tick(); return 0; }
    return DefWindowProcW(h, m, w, l);
}

void TaskbarModule::applyEngine() {
    engine_.update(saved_);
    overlay_.update(saved_);
    if (app_) app_->changed();
}

void TaskbarModule::start() {
    if (started_) return;
    started_ = true;
    engine_.debugText = [this] { return overlay_.debugText(); };
    WNDCLASSEXW wc{}; wc.cbSize = sizeof wc; wc.lpfnWndProc = timerProc; wc.hInstance = GetModuleHandleW(nullptr); wc.lpszClassName = L"GhostStyleTbTimer";
    RegisterClassExW(&wc);
    timerWin_ = CreateWindowExW(0, wc.lpszClassName, L"", 0, 0, 0, 0, 0, HWND_MESSAGE, nullptr, wc.hInstance, nullptr);
    SetWindowLongPtrW(timerWin_, GWLP_USERDATA, (LONG_PTR)this);
    SetTimer(timerWin_, 1, 40, nullptr);
    if (saved_.active()) applyEngine();
}
void TaskbarModule::stop() {
    if (!started_) return;
    engine_.shutdown();   // сначала возвращаем родной фон панели, потом убираем плашки: панель ни на миг не остаётся прозрачной без них
    overlay_.update(Appearance{});
}

void TaskbarModule::revert() {
    saved_.Shape = "none"; saved_.UseXaml = false; saveAppearance(saved_); draft_ = saved_; shapeSel_ = 0;
    std::string err; int n = regReset(err);
    if (started_) { engine_.update(saved_); engine_.shutdown(); overlay_.update(saved_); }
    status_ = L("Панель возвращена как в Windows.");
    (void)n;
}

void TaskbarModule::apply() {
    saved_ = draft_;
    saved_.normalize();
    saved_.UseXaml = saved_.Shape != "none" && Engine::unavailableReason().empty();
    saved_.XamlConsentShown = true;
    saveAppearance(saved_);
    {   // цвет плашек — в общий файл, который читает окно плашек
        using gs::Json; Json j = Json::Object();
        j.set("Active", Json::Boolean(cOn_)); j.set("BgMode", Json::String(cOn_ ? "solid" : "default")); j.set("BgColor", Json::String(cColor_));
        j.set("BgOpacity", Json::Number(cOp_)); j.set("HideStroke", Json::Boolean(false));
        std::string out; gs::jsonDump(j, out); gs::makeDirs(gs::appDataDir()); gs::writeFileAtomic(gs::appDataDir() + L"\\color-plus-taskbar.json", out);
    }
    if (started_) applyEngine();
    status_ = saved_.Shape == "none" ? L("Панель возвращена как в Windows.") : L("Применено. Панель подключается — это занимает несколько секунд.");
}

bool TaskbarModule::skinCapture(gs::Json& o) {
    if (saved_.Shape == "none") return false;
    using gs::Json; o = Json::Object();
    o.set("shape", Json::String(saved_.Shape)); o.set("radius", Json::Number(saved_.Radius)); o.set("sideMargin", Json::Number(saved_.SideMargin)); o.set("bottomGap", Json::Number(saved_.BottomGap));
    o.set("islandStyle", Json::String(saved_.IslandStyle)); o.set("islandPadding", Json::Number(saved_.IslandPadding)); o.set("islandMode", Json::String(saved_.IslandMode)); o.set("islandGap", Json::Number(saved_.IslandGap)); o.set("keepEdges", Json::Boolean(saved_.KeepEdges));
    return true;
}
std::string TaskbarModule::skinApply(const gs::Json& p) {
    Appearance a = saved_;
    a.Shape = p.str("shape", "none"); a.Radius = p.num("radius", a.Radius); a.SideMargin = p.num("sideMargin", a.SideMargin); a.BottomGap = p.num("bottomGap", a.BottomGap);
    a.IslandStyle = p.str("islandStyle", a.IslandStyle); a.IslandPadding = p.num("islandPadding", a.IslandPadding); a.IslandMode = p.str("islandMode", a.IslandMode); a.IslandGap = p.num("islandGap", a.IslandGap); a.KeepEdges = p.boolean("keepEdges", a.KeepEdges);
    a.normalize();
    if (a.Shape != "none" && !Engine::unavailableReason().empty()) return Engine::unavailableReason();
    draft_ = a; shapeSel_ = selFrom(draft_); apply();
    return "";
}

void TaskbarModule::init(ID3D11Device* dev) { canvas_.init(dev, 640, 190); }
void TaskbarModule::tick(double) { if (canvas_.ready()) canvas_.draw([this](sp::Gfx& g) { drawPreview(g); }); }

// Предпросмотр: выдуманный рабочий стол и панель; плашки считает тот же buildShapes, что и для настоящей панели.
void TaskbarModule::drawPreview(sp::Gfx& g) {
    const float W = 640, H = 190, barH = 44;
    g.rect(0, 0, W, H, sp::Rgb{14, 12, 34}, 1);
    g.rect(0, 0, W, H * 0.55f, sp::Rgb{26, 18, 70}, 1);
    g.roundRect(50, 24, 330, 120, 6, sp::Rgb{40, 36, 86}, 1); g.rect(50, 24, 330, 38, sp::Rgb{0, 240, 255}, 0.35f);
    g.roundRect(350, 50, 600, 126, 6, sp::Rgb{58, 30, 90}, 1); g.rect(350, 50, 600, 62, sp::Rgb{255, 42, 157}, 0.35f);
    float y0 = H - barH;
    g.rect(0, y0, W, H, sp::Rgb{10, 8, 22}, 0.9f);
    BarGeometry geo; geo.rootW = (int)W; geo.rootH = (int)barH; geo.scale100 = 100;
    geo.widgets = Box{8, 4, 100, 40}; geo.system = Box{500, 4, 632, 40};
    int n = 7, iconW = 40, gap = 6, total = n * iconW + (n - 1) * gap, x0 = (int)(W - total) / 2 - 20;
    geo.apps = Box{x0, 4, x0 + total, 40};
    for (int i = 0; i < n; i++) geo.icons.push_back(Box{x0 + i * (iconW + gap), 4, x0 + i * (iconW + gap) + iconW, 40});
    Appearance a = draft_; a.normalize();
    auto rects = buildShapes(a, (int)W, (int)barH, 1.0, geo);
    Fill f = resolveFill();
    ColorLink cl = colorLinkCurrent();
    sp::Rgb col{(float)(f.r * 255), (float)(f.g * 255), (float)(f.b * 255)};
    if (cOn_) { sp::Rgb c = sp::parseHex(cColor_); col = c; f.a = std::clamp(cOp_ / 100.0, 0.05, 1.0); f.has = true; }
    float alpha = a.Shape == "none" ? 0.f : (float)std::max(0.35, f.a);
    if (!f.has) alpha = 0.f;
    (void)cl;
    for (auto& r : rects) if (alpha > 0) g.roundRect((float)r.x, y0 + (float)r.y, (float)(r.x + r.w), std::min(H, y0 + (float)(r.y + r.h)), (float)r.r, col, alpha);
    sp::Rgb ic[] = {{0, 240, 255}, {255, 42, 157}, {252, 238, 10}, {162, 89, 255}, {60, 255, 154}, {255, 138, 61}, {230, 230, 240}};
    for (int i = 0; i < n; i++) g.roundRect((float)geo.icons[i].L + 6, y0 + 10, (float)geo.icons[i].R - 6, y0 + 34, 6, ic[i % 7], 0.95f);
    for (int i = 0; i < 3; i++) g.rect(512.f + i * 36, y0 + 16, 530.f + i * 36, y0 + 28, sp::Rgb{200, 200, 220}, 0.7f);
    g.rect(12, y0 + 14, 90, y0 + 30, sp::Rgb{200, 200, 220}, 0.45f);
}

void TaskbarModule::drawPreview() { if (canvas_.ready()) imageFit((ImTextureID)(intptr_t)canvas_.srv(), 640, 190); }

void TaskbarModule::drawPanel() {
    ImGui::PushFont(mono()); ImGui::PushStyleColor(ImGuiCol_Text, rgb(200, 208, 218)); ImGui::TextUnformatted(L("Панель")); ImGui::PopStyleColor(); ImGui::PopFont();
    note("Панель задач Windows 11 по-своему: плавающая, «вкладка» или острова под значками. Выберите форму, посмотрите на предпросмотре и нажмите «Применить». Пока не нажали, с панелью ничего не происходит.");
    std::string un = Engine::unavailableReason();
    if (!un.empty()) { ImGui::PushStyleColor(ImGuiCol_Text, rgb(255, 176, 32)); ImGui::TextWrapped("%s", L(un.c_str())); ImGui::PopStyleColor(); }
    ImGui::Spacing();
    if (beginCard("1. Форма панели")) {
        static const char* shapes[] = {"Обычная", "Плавающая", "Острова", "Вкладка", "Вкладки-острова"};
        if (seg("shape", shapes, 5, shapeSel_)) presetFrom(shapeSel_);
        ImGui::Spacing();
        if (draft_.Shape != "none") {
            slider("Скругление", "насколько круглые углы плашек", draft_.Radius, 0, 30);
            if (draft_.Shape == "pill" || (draft_.Shape == "islands" && draft_.IslandStyle == "float")) slider("Зазор снизу", "расстояние от плашки до нижнего края экрана", draft_.BottomGap, 0, 20);
            slider("Поля вокруг значков", "насколько плашка шире значков", draft_.IslandPadding, 0, 40);
            if (draft_.Shape == "islands") {
                int m = draft_.IslandMode == "group" ? 1 : 0; static const char* modes[] = {"Каждый значок", "Группы значков"};
                rowTitle("Острова", "остров вокруг каждого значка или вокруг соседних групп");
                if (seg("mode", modes, 2, m)) draft_.IslandMode = m == 1 ? "group" : "each";
                if (draft_.IslandMode == "each") slider("Зазор между островами", "расстояние между соседними значками", draft_.IslandGap, 0, 40);
            }
            if (draft_.Shape == "pill" || draft_.Shape == "tab") toggle("Часы и трей отдельно", "часы, сеть и трей остаются отдельными островами (выкл. — одна общая плашка)", draft_.KeepEdges);
            toggle("Остров под виджетами", "плашка под кнопкой виджетов слева (у части компьютеров там пусто)", draft_.WidgetsIsland);
        }
    }
    endCard();
    if (beginCard("2. Цвет плашек")) {
        toggle("Свой цвет", "выключено — цвет под тему Windows (тёмный или светлый)", cOn_);
        if (cOn_) {
            sp::Rgb c = sp::parseHex(cColor_); float col[3] = {c.r / 255.f, c.g / 255.f, c.b / 255.f};
            if (ImGui::ColorPicker3("##tbc", col, ImGuiColorEditFlags_NoSidePreview | ImGuiColorEditFlags_NoInputs | ImGuiColorEditFlags_PickerHueWheel)) cColor_ = sp::toHex({col[0] * 255, col[1] * 255, col[2] * 255});
            slider("Непрозрачность", "100 — сплошной цвет, меньше — просвечивает", cOp_, 10, 100);
        }
    }
    endCard();
    ImGui::PushStyleColor(ImGuiCol_Button, rgb(200, 208, 218)); ImGui::PushStyleColor(ImGuiCol_Text, rgb(6, 5, 13));
    if (ImGui::Button(L("Применить"))) apply();
    ImGui::PopStyleColor(2);
    ImGui::SameLine();
    if (ImGui::Button(L("Вернуть как было"))) revert();
    std::string st = engine_.status(); std::string nt = overlay_.note();
    ImGui::PushStyleColor(ImGuiCol_Text, muted());
    if (!status_.empty()) ImGui::TextWrapped("%s", L(status_.c_str()));
    if (!st.empty()) ImGui::TextWrapped("%s", L(st.c_str()));
    if (!nt.empty()) ImGui::TextWrapped("%s", L(nt.c_str()));
    if (!reportMsg_.empty()) ImGui::TextWrapped("%s", L(reportMsg_.c_str()));
    ImGui::PopStyleColor();
    if (engine_.needsRestart()) { if (ImGui::Button(L("Перезапустить панель задач"))) { engine_.noteExplorerRestart(); explorerRestart(); } }
    if (engine_.blocked()) { ImGui::SameLine(); if (ImGui::Button(L("Попробовать снова"))) { engine_.clearBlock(); applyEngine(); } }
    ImGui::Spacing();
    if (ImGui::CollapsingHeader(L("Ещё настройки"))) {
        for (auto& o : regOptions()) {
            if (!regAvailable(o)) continue;
            ImGui::PushID(o.id);
            rowTitle(o.title, o.hint);
            int cur = regCurrent(o), sel = 0; for (size_t i = 0; i < o.values.size(); i++) if (o.values[i] == cur) sel = (int)i;
            if (seg("v", o.labels.data(), (int)o.labels.size(), sel)) { std::string e = regSet(o, o.values[sel]); status_ = e.empty() ? (o.needsRestart ? "Изменено. Чтобы увидеть результат, перезапустите панель задач." : "Изменено.") : e; }
            ImGui::PopID();
            ImGui::Spacing();
        }
        bool ah = autoHideGet();
        if (toggle("Автоскрытие панели", "панель прячется и выезжает, когда курсор у края экрана", ah)) autoHideSet(ah);
        if (!animationsOn()) { note("«Эффекты анимации» в Windows выключены — панель при автоскрытии появляется мгновенно."); if (ImGui::Button(L("Включить анимации Windows"))) animationsEnable(); }
        bool ss = draft_.ShowStroke; if (toggle("Тонкая линия по краю панели", "по умолчанию скрыта", ss)) { draft_.ShowStroke = ss; saved_.ShowStroke = ss; saveAppearance(saved_); if (started_) applyEngine(); }
        bool ar = saved_.AutoReport; if (toggle("Отправлять отчёт разработчику сам", "один раз после подключения: версии, настройки и названия элементов панели (без имени пользователя и компьютера)", ar)) { saved_.AutoReport = ar; draft_.AutoReport = ar; saveAppearance(saved_); if (started_) applyEngine(); }
        if (ImGui::Button(L("Перезапустить панель задач"))) { engine_.noteExplorerRestart(); explorerRestart(); }
        ImGui::SameLine();
        if (ImGui::Button(L(jobBusy_ ? "Собираю отчёт…" : "Сохранить отчёт на рабочий стол")) && !jobBusy_) {
            jobBusy_ = true; if (job_.joinable()) job_.join();
            job_ = std::thread([this] { std::wstring p; bool ok = engine_.saveReport(p); std::lock_guard<std::mutex> g(jobMu_); jobResult_ = ok ? "Отчёт сохранён: TaskbarPlus-report.txt на рабочем столе." : "Не получилось сохранить отчёт."; jobBusy_ = false; });
        }
        ImGui::SameLine();
        if (ImGui::Button(L("Отправить отчёт разработчику")) && !jobBusy_) {
            jobBusy_ = true; if (job_.joinable()) job_.join();
            job_ = std::thread([this] { std::string e = engine_.uploadReport(); std::lock_guard<std::mutex> g(jobMu_); jobResult_ = e.empty() ? "Отчёт отправлен." : "Не отправилось: " + e; jobBusy_ = false; });
        }
        { std::lock_guard<std::mutex> g(jobMu_); if (!jobResult_.empty() && !jobBusy_) { reportMsg_ = jobResult_; jobResult_.clear(); } }
    }
}

// ---------- проверка ----------
int TaskbarModule::selfTest(const std::wstring& dir, std::string& log) {
    int fails = 0;
    auto check = [&](const std::string& name, bool ok, const std::string& extra = "") { log += (ok ? "PASS " : "FAIL ") + name + (extra.empty() ? "" : " — " + extra) + "\n"; if (!ok) fails++; };
    fails += runLogicChecks(log);
    check("список параметров реестра не пуст", regOptions().size() >= 8);
    Appearance a; a.Shape = "pill"; a.Radius = 99; a.normalize();
    check("настройки приводятся к допустимым (скругление ≤ 30)", a.Radius == 30);
    std::string un = Engine::unavailableReason();
    if (!un.empty()) { log += "ПРОПУСК показа: " + un + "\n"; return fails; }

    Appearance orig = loadAppearance();
    Appearance live; live.Shape = "pill"; live.Radius = 16; live.IslandPadding = 10; live.UseXaml = true; live.XamlConsentShown = true; live.AutoReport = false; live.KeepEdges = true;
    engine_.debugText = [this] { return overlay_.debugText(); };
    engine_.update(live); overlay_.update(live);
    ULONGLONG end = GetTickCount64() + 90000;
    while (GetTickCount64() < end) { MSG m; while (PeekMessageW(&m, nullptr, 0, 0, PM_REMOVE)) { TranslateMessage(&m); DispatchMessageW(&m); } overlay_.tick(); if (engine_.ready() && engine_.barTransparent() && overlay_.anyShown()) break; Sleep(40); }
    log += "состояние движка: " + engine_.status() + "\n" + overlay_.debugText();
    check("движок подключился и панель стала прозрачной", engine_.ready() && engine_.barTransparent());
    check("окно с плашками показано", overlay_.anyShown());
    for (int i = 0; i < 60; i++) { overlay_.tick(); Sleep(40); }
    std::vector<RoundRect> plan; bool have = overlay_.lastPlan(plan);
    check("плашки посчитаны по настоящим значкам", have && !plan.empty(), "плашек " + std::to_string(plan.size()));
    log += overlay_.describeWindows();
    check("окно плашек лежит сразу под панелью", overlay_.describeWindows().find("сразу под панелью: да") != std::string::npos);
    engine_.shutdown(); overlay_.update(Appearance{});
    std::string t; gs::readFile(gs::appDataDir() + L"\\taskbar-plus-engine-status.txt", t);
    check("после выключения панель отдана Windows (applied=0)", t.find("applied=0") != std::string::npos || t.empty(), t.substr(0, 120));
    saveAppearance(orig);
    (void)dir;
    return fails;
}

}  // namespace tb
