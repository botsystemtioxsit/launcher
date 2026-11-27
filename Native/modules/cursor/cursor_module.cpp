#include "cursor_module.hpp"
#include <cmath>
#include "cursor_apply.hpp"
#include "cursor_files.hpp"
#include "win.hpp"
#include "cursor_shapes.hpp"
#include "imgui.h"
#include "loc.hpp"
#include "overlay_host.hpp"
#include "snapshots.hpp"
#include "ui_common.hpp"

namespace sp {

using gs::L;
using namespace ui;

static const char* kTrailNames[] = {"Нет", "Комета", "Искры", "Свечение", "Звёзды", "Пузырьки", "Сердечки", "Призраки"};
static const char* kTrailIds[] = {"none", "comet", "sparks", "glow", "stars", "bubbles", "hearts", "ghost"};
static const char* kModeNames[] = {"Как курсор", "Радуга", "Два цвета", "Свой цвет"};
static const char* kModeIds[] = {"cursor", "rainbow", "two", "custom"};
static const char* kClickNames[] = {"Нет", "Круги", "Вспышка"};
static const char* kClickIds[] = {"none", "ring", "burst"};
static const char* kSwatches[] = {"#FF2A9D", "#FF4D4D", "#FF8A3D", "#FFB020", "#F5D90A", "#20C27A", "#00F0FF", "#1E6BFF", "#6C4DFF", "#C04DFF", "#F2F2F2", "#101018"};
static int indexOf(const char* const* ids, int n, const std::string& v) { for (int i = 0; i < n; i++) if (v == ids[i]) return i; return 0; }

CursorModule::CursorModule() {
    draft_ = CursorSettings::load();
    saved_ = draft_;
    trail_.setSettings(saved_);
}

void CursorModule::init(ID3D11Device* dev) {
    canvas_.init(dev, 640, 240);
    for (auto& t : thumbs_) t.init(dev, 64, 64);
    syncPreview();
    thumbsDirty_ = true;
}

void CursorModule::shutdownUi() {
    canvas_.release();
    for (auto& t : thumbs_) t.release();
}

void CursorModule::syncPreview() {
    CursorSettings p = draft_;
    p.OverlayOn = true;
    preview_.setSettings(p);
}

void CursorModule::changed() {
    syncPreview();
    thumbsDirty_ = true;
    status_ = L("Предпросмотр обновлён. Чтобы это появилось в Windows, нажмите «Применить».");
}

void CursorModule::drawThumbs() {
    Rgb fill = draft_.fillRgb(), line = draft_.lineRgb();
    for (size_t i = 0; i < thumbs_.size(); i++) {
        if (!thumbs_[i].ready()) continue;
        std::string id = kShapes[i].id;
        thumbs_[i].draw([&](Gfx& g) {
            g.roundRect(0, 0, 64, 64, 8, {18, 14, 40}, 1.f);
            g.setTransform(D2D1::Matrix3x2F::Scale(1.5f, 1.5f) * D2D1::Matrix3x2F::Translation(8, 8));
            if (id == "windows") drawShape(g, "classic", fill, line); else drawShape(g, id, fill, line);
            g.setTransform(D2D1::Matrix3x2F::Identity());
        });
    }
    thumbsDirty_ = false;
}

void CursorModule::drawPreview(Gfx& g) {
    const float W = 640, H = 240;
    for (int i = 0; i < 24; i++) g.rect(0, H * i / 24, W, H * (i + 1) / 24 + 1, Rgb::lerp({14, 10, 34}, {30, 20, 70}, i / 23.f), 1.f);
    for (int x = 0; x < (int)W; x += 40) g.line((float)x, 0, (float)x, H, 1.f, {40, 30, 90}, 0.35f);
    for (int y = 0; y < (int)H; y += 40) g.line(0, (float)y, W, (float)y, 1.f, {40, 30, 90}, 0.35f);
    Env env; env.vw = 640; env.vh = 240;
    preview_.draw(g, env);
    float x = 320 + 240 * std::sin((float)simT_ * 0.9f), y = 120 + 80 * std::sin((float)simT_ * 1.7f);
    std::string shape = draft_.Shape == "windows" || draft_.Shape.rfind("mine:", 0) == 0 ? "classic" : draft_.Shape;
    float hx, hy; shapeHot(shape, hx, hy);
    float k = 2.f * draft_.Size / 100.f;
    g.setTransform(D2D1::Matrix3x2F::Scale(k, k) * D2D1::Matrix3x2F::Translation(x - hx * 32 * k, y - hy * 32 * k));
    drawShape(g, shape, draft_.fillRgb(), draft_.lineRgb());
    g.setTransform(D2D1::Matrix3x2F::Identity());
}

void CursorModule::tick(double dt) {
    if (!canvas_.ready()) return;
    simT_ += dt; clickT_ += dt;
    Env env; env.dt = dt; env.time = simT_; env.vw = 640; env.vh = 240;
    float x = 320 + 240 * std::sin((float)simT_ * 0.9f), y = 120 + 80 * std::sin((float)simT_ * 1.7f);
    env.cursor.x = (LONG)x; env.cursor.y = (LONG)y;
    if (clickT_ > 2.5) { clickT_ = 0; env.lclick = true; env.lbtn = true; }
    preview_.update(env);
    canvas_.draw([this](Gfx& g) { drawPreview(g); });
    if (thumbsDirty_) drawThumbs();
}

std::string CursorModule::applyNow(const CursorSettings& s) {
    CursorSettings n = s; n.normalize();
    std::string err = applyCursors(n);
    n.Applied = true; n.OverlayOn = n.wantsOverlay();
    saved_ = n; draft_ = n;
    saved_.save();
    trail_.setSettings(saved_);
    trail_.clear();
    if (app_ && app_->changed) app_->changed();
    return err;
}

void CursorModule::revert() {
    if (saved_.Applied) revertCursors();
    saved_.Applied = false; saved_.OverlayOn = false; draft_.Applied = false; draft_.OverlayOn = false;
    saved_.save();
    trail_.setSettings(saved_);
    trail_.clear();
    if (app_ && app_->changed) app_->changed();
}

int CursorModule::snapshots(const std::wstring& dir, std::string& log) {
    // таблица форм: каждая форма в трёх размерах на тёмном фоне
    int written = 0;
    CursorSettings s = draft_;
    const char* shapes[] = {"classic", "round", "neon", "dot", "ring", "cross", "ghost", "star", "ibeam", "crosshair", "no"};
    const int cell = 96, cols = 11;
    std::vector<unsigned char> sheet((size_t)cell * cols * cell * 4, 0);
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    for (int i = 0; i < cols; i++) {
        CurImg im;
        if (!renderShapeImg(shapes[i], 64, s.fillRgb(), s.lineRgb(), im)) { log += std::string("форма не нарисовалась: ") + shapes[i] + "\n"; continue; }
        for (int y = 0; y < cell; y++) for (int x = 0; x < cell; x++) {
            size_t o = ((size_t)y * cell * cols + (size_t)i * cell + x) * 4;
            sheet[o] = 40; sheet[o + 1] = 20; sheet[o + 2] = 18; sheet[o + 3] = 255;
        }
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) {
            size_t o = ((size_t)(y + 16) * cell * cols + (size_t)i * cell + x + 16) * 4, si = ((size_t)y * 64 + x) * 4;
            int a = im.bgra[si + 3];
            for (int c = 0; c < 3; c++) sheet[o + c] = (unsigned char)((im.bgra[si + c] * a + sheet[o + c] * (255 - a)) / 255);
        }
    }
    if (savePngBgra(dir + L"\\cursor-shapes.png", cell * cols, cell, sheet.data(), cell * cols * 4)) written++;
    // .cur: собираем и проверяем, что Windows его читает
    std::vector<CurImg> imgs;
    for (int px : {32, 64, 128}) { CurImg im; if (renderShapeImg("classic", px, s.fillRgb(), s.lineRgb(), im)) imgs.push_back(im); }
    std::string cur = buildCur(imgs);
    std::wstring path = dir + L"\\test-classic.cur";
    gs::writeFileAtomic(path, cur);
    HCURSOR hc = (HCURSOR)LoadImageW(nullptr, path.c_str(), IMAGE_CURSOR, 64, 64, LR_LOADFROMFILE);
    log += std::string("файл .cur: ") + std::to_string(cur.size()) + " байт; Windows его читает: " + (hc ? "да" : "нет") + "\n";
    if (hc) {
        ICONINFO ii{}; GetIconInfo(hc, &ii);
        log += "  горячая точка: " + std::to_string(ii.xHotspot) + "," + std::to_string(ii.yHotspot) + "\n";
        DestroyCursor(hc);
    }
    // системный указатель: читаем и перекрашиваем
    wchar_t win[MAX_PATH] = {}; GetWindowsDirectoryW(win, MAX_PATH);
    CurImg sysImg;
    bool ok = loadSystemCursorImg(std::wstring(win) + L"\\Cursors\\aero_arrow.cur", 64, sysImg);
    log += std::string("системная стрелка прочитана: ") + (ok ? "да" : "нет") + "\n";
    if (ok) { size_t lit = 0; for (size_t i = 3; i < sysImg.bgra.size(); i += 4) if (sysImg.bgra[i] > 0) lit++; log += "  непрозрачных пикселей: " + std::to_string(lit) + "\n"; }
    return written;
}

// Настоящая запись в Windows и возврат — на машине GitHub (там можно, пользователь одноразовый).
int CursorModule::selfTest(const std::wstring& dir, std::string& log) {
    int fails = 0;
    auto check = [&](const std::string& name, bool ok, const std::string& extra = "") { log += (ok ? "PASS " : "FAIL ") + name + (extra.empty() ? "" : " — " + extra) + "\n"; if (!ok) fails++; };
    auto reg = [](const wchar_t* name, std::wstring& v) {
        HKEY k; if (RegOpenKeyExW(HKEY_CURRENT_USER, L"Control Panel\\Cursors", 0, KEY_QUERY_VALUE, &k) != ERROR_SUCCESS) return false;
        wchar_t buf[1024]; DWORD sz = sizeof buf, type = 0;
        bool ok = RegQueryValueExW(k, name, nullptr, &type, (BYTE*)buf, &sz) == ERROR_SUCCESS; RegCloseKey(k); if (ok) v = buf; return ok; };
    auto dword = [](const wchar_t* key, const wchar_t* name, DWORD& v) {
        HKEY k; if (RegOpenKeyExW(HKEY_CURRENT_USER, key, 0, KEY_QUERY_VALUE, &k) != ERROR_SUCCESS) return false;
        DWORD sz = sizeof v, type = 0; bool ok = RegQueryValueExW(k, name, nullptr, &type, (BYTE*)&v, &sz) == ERROR_SUCCESS; RegCloseKey(k); return ok; };
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);

    CursorSettings s0 = CursorSettings::fromJson(gs::Json::Object());
    std::string out0; gs::jsonDump(s0.toJson(), out0);
    gs::Json back; bool pj = gs::jsonParse(out0, back);
    CursorSettings s1 = CursorSettings::fromJson(back);
    check("настройки записываются и читаются", pj && s1.Shape == s0.Shape && s1.Fill == s0.Fill && s1.TrailLength == s0.TrailLength);
    gs::Json old; gs::jsonParse("{\"Shape\":\"nonsense\",\"Fill\":\"red\",\"Size\":999,\"Trail\":\"comet\"}", old);
    CursorSettings s2 = CursorSettings::fromJson(old);
    check("мусор в настройках заменяется безопасным", s2.Shape == "classic" && s2.Fill == "#FF2A9D" && s2.Size == 300);

    std::wstring beforeArrow, beforeHand; bool hadArrow = reg(L"Arrow", beforeArrow), hadHand = reg(L"Hand", beforeHand);
    DWORD baseBefore = 0; bool hadBase = dword(L"Control Panel\\Cursors", L"CursorBaseSize", baseBefore);

    CursorSettings t; t.Shape = "classic"; t.Fill = "#FF2A9D"; t.Size = 150; t.All = true; t.Trail = "comet"; t.normalize();
    std::string err = applyNow(t);
    check("применение без ошибок", err.empty(), err);
    std::wstring arrow, hand, ibeam;
    bool a = reg(L"Arrow", arrow), h = reg(L"Hand", hand), ib = reg(L"IBeam", ibeam);
    check("в реестре появились наши указатели (стрелка, рука, текст)", a && h && ib && arrow.find(L"cursor-plus") != std::wstring::npos);
    check("файл стрелки существует", a && GetFileAttributesW(arrow.c_str()) != INVALID_FILE_ATTRIBUTES);
    HCURSOR hc = a ? (HCURSOR)LoadImageW(nullptr, arrow.c_str(), IMAGE_CURSOR, 64, 64, LR_LOADFROMFILE) : nullptr;
    check("Windows читает наш файл .cur", hc != nullptr);
    if (hc) {
        ICONINFO ii{}; GetIconInfo(hc, &ii);
        check("горячая точка у стрелки на месте (кончик, не центр)", ii.xHotspot <= 8 && ii.yHotspot <= 8, std::to_string(ii.xHotspot) + "," + std::to_string(ii.yHotspot));
        DestroyCursor(hc);
    }
    DWORD base = 0; bool hb = dword(L"Control Panel\\Cursors", L"CursorBaseSize", base);
    check("размер указателя 150% → 48", hb && base == 48, std::to_string(base));
    check("настройки сохранены как применённые", CursorSettings::load().Applied);

    // «Как в Windows»: системные указатели перекрашиваются
    CursorSettings w = t; w.Shape = "windows"; w.Size = 100;
    err = applyNow(w);
    std::wstring arrow2; bool a2 = reg(L"Arrow", arrow2);
    check("форма «Как в Windows» тоже применяется", err.empty() && a2 && GetFileAttributesW(arrow2.c_str()) != INVALID_FILE_ATTRIBUTES, err);

    revert();
    std::wstring afterArrow, afterHand; bool hasA = reg(L"Arrow", afterArrow), hasH = reg(L"Hand", afterHand);
    check("после «Вернуть как было» стрелка как раньше", hasA == hadArrow && (!hadArrow || afterArrow == beforeArrow));
    check("после «Вернуть как было» рука как раньше", hasH == hadHand && (!hadHand || afterHand == beforeHand));
    DWORD baseAfter = 0; bool hb2 = dword(L"Control Panel\\Cursors", L"CursorBaseSize", baseAfter);
    check("размер указателя возвращён", hb2 == hadBase && (!hadBase || baseAfter == baseBefore));
    check("файлы наших указателей удалены", GetFileAttributesW(arrow2.c_str()) == INVALID_FILE_ATTRIBUTES);
    check("настройки помечены как не применённые", !CursorSettings::load().Applied);
    (void)dir;
    return fails;
}

void CursorModule::prepareOverlayTest() {
    CursorSettings s; s.Trail = "comet"; s.OverlayOn = true; s.Applied = false; s.Click = "ring"; s.normalize();
    trail_.setSettings(s);
}

void CursorModule::drawPreview() {
    note("Курсор движется сам: так будет выглядеть шлейф. Раз в несколько секунд — клик с эффектом.");
    if (canvas_.ready()) imageFit((ImTextureID)(intptr_t)canvas_.srv(), 640, 240);
}

void CursorModule::drawPanel() {
    ImGui::PushFont(mono());
    ImGui::PushStyleColor(ImGuiCol_Text, rgb(0, 240, 255));
    ImGui::TextUnformatted(L("Курсор"));
    ImGui::PopStyleColor(); ImGui::PopFont();
    note("Свой курсор: цвет, форма, шлейф за мышью и эффекты при клике. Выберите — ниже сразу видно, как будет. Понравилось — «Применить». Пока не нажали, в Windows ничего не меняется.");
    ImGui::Spacing();

    if (beginCard("2. Курсор")) {
        rowTitle("Форма", "");
        for (size_t i = 0; i < kShapes.size(); i++) {
            ImGui::PushID((int)i);
            bool on = draft_.Shape == kShapes[i].id;
            if (on) ImGui::PushStyleColor(ImGuiCol_Button, rgb(0, 240, 255));
            ImVec2 sz(64, 64);
            ImGui::BeginGroup();
            bool clicked = thumbs_[i].ready()
                ? ImGui::ImageButton("##s", (ImTextureID)(intptr_t)thumbs_[i].srv(), sz, ImVec2(0, 0), ImVec2(1, 1), on ? rgb(0, 240, 255, 0.35f) : ImVec4(0, 0, 0, 0))
                : ImGui::Button(L(kShapes[i].title), sz);
            ImGui::TextUnformatted(L(kShapes[i].title));
            ImGui::EndGroup();
            if (on) ImGui::PopStyleColor();
            if (clicked) { draft_.Shape = kShapes[i].id; changed(); }
            ImGui::PopID();
            if (i + 1 < kShapes.size()) ImGui::SameLine();
        }
        ImGui::Spacing();
        rowTitle("Цвет курсора", "");
        float col[3] = {draft_.fillRgb().r / 255.f, draft_.fillRgb().g / 255.f, draft_.fillRgb().b / 255.f};
        if (ImGui::ColorEdit3("##fill", col, ImGuiColorEditFlags_NoInputs | ImGuiColorEditFlags_PickerHueWheel)) { draft_.Fill = toHex({col[0] * 255, col[1] * 255, col[2] * 255}); changed(); }
        for (int i = 0; i < 12; i++) {
            ImGui::SameLine();
            ImGui::PushID(100 + i);
            Rgb c = parseHex(kSwatches[i]);
            if (ImGui::ColorButton("##sw", ImVec4(c.r / 255.f, c.g / 255.f, c.b / 255.f, 1), ImGuiColorEditFlags_NoTooltip, ImVec2(26, 26))) { draft_.Fill = kSwatches[i]; changed(); }
            ImGui::PopID();
        }
        ImGui::Spacing();
        bool autoLine = draft_.Outline == "auto";
        if (toggle("Обводка автоматически", "тёмный оттенок цвета курсора", autoLine)) { draft_.Outline = autoLine ? "auto" : toHex(draft_.lineRgb()); changed(); }
        if (!autoLine) {
            float oc[3] = {draft_.lineRgb().r / 255.f, draft_.lineRgb().g / 255.f, draft_.lineRgb().b / 255.f};
            if (ImGui::ColorEdit3("##line", oc, ImGuiColorEditFlags_NoInputs)) { draft_.Outline = toHex({oc[0] * 255, oc[1] * 255, oc[2] * 255}); changed(); }
        }
        if (slider("Размер", "в процентах: 100 — обычный", draft_.Size, 100, 300)) changed();
        if (toggle("Перекрасить и остальные указатели", "Рука, текст, стрелки изменения размера, «недоступно» и другие тоже получат ваш цвет. Крутящийся указатель загрузки остаётся как в Windows.", draft_.All)) changed();
    }
    endCard();

    if (beginCard("3. Шлейф за курсором")) {
        int t = indexOf(kTrailIds, 8, draft_.Trail);
        rowTitle("Вид", "");
        if (seg("trail", kTrailNames, 8, t)) { draft_.Trail = kTrailIds[t]; changed(); }
        ImGui::Spacing();
        int m = indexOf(kModeIds, 4, draft_.TrailColorMode);
        rowTitle("Цвет шлейфа", "«Как курсор» — цвет курсора; «Радуга» — переливается; «Два цвета» — плавный переход от первого ко второму.");
        if (seg("mode", kModeNames, 4, m)) { draft_.TrailColorMode = kModeIds[m]; changed(); }
        if (draft_.TrailColorMode == "two" || draft_.TrailColorMode == "custom") {
            float c1[3] = {draft_.trailRgb().r / 255.f, draft_.trailRgb().g / 255.f, draft_.trailRgb().b / 255.f};
            if (ImGui::ColorEdit3("##t1", c1, ImGuiColorEditFlags_NoInputs)) { draft_.TrailColor = toHex({c1[0] * 255, c1[1] * 255, c1[2] * 255}); changed(); }
            if (draft_.TrailColorMode == "two") {
                ImGui::SameLine();
                float c2[3] = {draft_.trailRgb2().r / 255.f, draft_.trailRgb2().g / 255.f, draft_.trailRgb2().b / 255.f};
                if (ImGui::ColorEdit3("##t2", c2, ImGuiColorEditFlags_NoInputs)) { draft_.TrailColor2 = toHex({c2[0] * 255, c2[1] * 255, c2[2] * 255}); changed(); }
            }
        }
        ImGui::Spacing();
        if (slider("Длина", "как долго шлейф остаётся за курсором", draft_.TrailLength, 0, 100)) changed();
        if (slider("Размер шлейфа", "", draft_.TrailSize, 0, 100)) changed();
    }
    endCard();

    if (beginCard("4. При клике")) {
        int c = indexOf(kClickIds, 3, draft_.Click);
        rowTitle("Эффект", "");
        if (seg("click", kClickNames, 3, c)) { draft_.Click = kClickIds[c]; changed(); }
    }
    endCard();

    ImGui::PushStyleColor(ImGuiCol_Button, rgb(0, 240, 255)); ImGui::PushStyleColor(ImGuiCol_Text, rgb(6, 5, 13));
    if (ImGui::Button(L("Применить"))) {
        std::string err = applyNow(draft_);
        status_ = err.empty() ? std::string(L("Применено. Если какая-то программа показывает старый курсор — перезапустите её.")) : std::string(L("Применилось не всё: ")) + err;
    }
    ImGui::PopStyleColor(2);
    ImGui::SameLine();
    if (ImGui::Button(L("Вернуть как было"))) { revert(); status_ = L("Всё возвращено: курсоры Windows как были, шлейф выключен."); }
    ImGui::PushStyleColor(ImGuiCol_Text, muted());
    ImGui::TextWrapped("%s", status_.empty() ? L(saved_.Applied ? "Применено." : "Не применено.") : status_.c_str());
    ImGui::PopStyleColor();
}

}  // namespace sp
