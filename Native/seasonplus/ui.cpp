#include "ui.hpp"
#include <cstdio>
#include "imgui.h"
#include "loc.hpp"
#include "scenes.hpp"
#include "win.hpp"

namespace sp {

using gs::L;

// ---------- предпросмотр ----------
bool Preview::init(ID3D11Device* dev) {
    D2D1_FACTORY_OPTIONS fo{};
    if (FAILED(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, __uuidof(ID2D1Factory1), &fo, (void**)fac_.GetAddressOf()))) return false;
    if (FAILED(DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory), (IUnknown**)dw_.GetAddressOf()))) return false;
    if (!gfx_.init(fac_.Get(), dw_.Get())) return false;
    ComPtr<IDXGIDevice> dxgi;
    if (FAILED(dev->QueryInterface(IID_PPV_ARGS(&dxgi)))) return false;
    if (FAILED(fac_->CreateDevice(dxgi.Get(), &dev_)) || FAILED(dev_->CreateDeviceContext(D2D1_DEVICE_CONTEXT_OPTIONS_NONE, &ctx_))) return false;
    D3D11_TEXTURE2D_DESC td{};
    td.Width = kW; td.Height = kH; td.MipLevels = 1; td.ArraySize = 1; td.Format = DXGI_FORMAT_B8G8R8A8_UNORM; td.SampleDesc.Count = 1;
    td.Usage = D3D11_USAGE_DEFAULT; td.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
    if (FAILED(dev->CreateTexture2D(&td, nullptr, &tex_)) || FAILED(dev->CreateShaderResourceView(tex_.Get(), nullptr, &srv_))) return false;
    ComPtr<IDXGISurface> surf;
    if (FAILED(tex_.As(&surf))) return false;
    D2D1_BITMAP_PROPERTIES1 bp = D2D1::BitmapProperties1(D2D1_BITMAP_OPTIONS_TARGET | D2D1_BITMAP_OPTIONS_CANNOT_DRAW, D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_PREMULTIPLIED));
    if (FAILED(ctx_->CreateBitmapFromDxgiSurface(surf.Get(), &bp, &bmp_))) return false;
    ctx_->SetTarget(bmp_.Get());
    eng_.W = kW; eng_.H = kH; eng_.OX = 0; eng_.OY = 0;
    return true;
}

void Preview::sync(const Settings& s) {
    eng_.S = s;
    eng_.setSurfaces(demoSurfaces(kW, kH));
    eng_.step(0.001);
    if (!warmed_) { warmed_ = true; eng_.warm(14); }
}

void Preview::tick(double dt) {
    if (!ctx_) return;
    eng_.step(dt);
    ctx_->BeginDraw();
    gfx_.begin(ctx_.Get());
    drawBackdrop(gfx_, kW, kH, eng_.season, eng_.holiday);
    drawScene(gfx_, eng_);
    ctx_->EndDraw();
}

// ---------- оформление (киберпанк, как у остальных плагинов) ----------
static ImFont* gMono = nullptr;

bool UiApp::loadFonts(float scale) {
    ImGuiIO& io = ImGui::GetIO();
    io.Fonts->Clear();
    ImFont* f = io.Fonts->AddFontFromFileTTF("C:\\Windows\\Fonts\\segoeui.ttf", 17.f * scale, nullptr, io.Fonts->GetGlyphRangesCyrillic());
    gMono = io.Fonts->AddFontFromFileTTF("C:\\Windows\\Fonts\\consolab.ttf", 22.f * scale, nullptr, io.Fonts->GetGlyphRangesCyrillic());
    if (!f) { io.Fonts->AddFontDefault(); }
    if (!gMono) gMono = io.Fonts->Fonts[0];
    return f != nullptr;
}

static ImVec4 rgb(int r, int g, int b, float a = 1.f) { return ImVec4(r / 255.f, g / 255.f, b / 255.f, a); }

void UiApp::setupStyle(float scale) {
    ImGuiStyle& s = ImGui::GetStyle();
    s = ImGuiStyle();
    s.WindowRounding = 0; s.ChildRounding = 4; s.FrameRounding = 3; s.GrabRounding = 3;
    s.FrameBorderSize = 1; s.ChildBorderSize = 1; s.WindowBorderSize = 0;
    s.ItemSpacing = ImVec2(8, 8); s.FramePadding = ImVec2(12, 6); s.WindowPadding = ImVec2(26, 22);
    s.ScaleAllSizes(scale);
    ImVec4* c = s.Colors;
    c[ImGuiCol_Text] = rgb(234, 248, 255); c[ImGuiCol_TextDisabled] = rgb(160, 143, 224);
    c[ImGuiCol_WindowBg] = rgb(7, 3, 20); c[ImGuiCol_ChildBg] = rgb(12, 8, 38);
    c[ImGuiCol_Border] = rgb(74, 34, 160);
    c[ImGuiCol_Button] = rgb(26, 16, 74); c[ImGuiCol_ButtonHovered] = rgb(50, 30, 120); c[ImGuiCol_ButtonActive] = rgb(0, 240, 255);
    c[ImGuiCol_FrameBg] = rgb(20, 12, 56); c[ImGuiCol_FrameBgHovered] = rgb(36, 22, 90); c[ImGuiCol_FrameBgActive] = rgb(50, 30, 120);
    c[ImGuiCol_SliderGrab] = rgb(0, 240, 255); c[ImGuiCol_SliderGrabActive] = rgb(255, 46, 151);
    c[ImGuiCol_CheckMark] = rgb(0, 240, 255);
    c[ImGuiCol_ScrollbarBg] = rgb(7, 3, 20); c[ImGuiCol_ScrollbarGrab] = rgb(74, 34, 160);
}

// ---------- кирпичики ----------
void UiApp::rowTitle(const char* title, const char* hint) {
    ImGui::TextUnformatted(L(title));
    if (hint && *hint) { ImGui::PushStyleColor(ImGuiCol_Text, ImGui::GetStyle().Colors[ImGuiCol_TextDisabled]); ImGui::TextWrapped("%s", L(hint)); ImGui::PopStyleColor(); }
}

bool UiApp::seg(const char* id, const char* const* labels, int n, int& sel) {
    bool ch = false;
    float avail = ImGui::GetContentRegionAvail().x, x = 0;
    ImGui::PushID(id);
    for (int i = 0; i < n; i++) {
        const char* t = L(labels[i]);
        float w = ImGui::CalcTextSize(t).x + ImGui::GetStyle().FramePadding.x * 2;
        if (i > 0) { if (x + w + 8 > avail) x = 0; else ImGui::SameLine(); }
        x += w + 8;
        bool on = sel == i;
        if (on) { ImGui::PushStyleColor(ImGuiCol_Button, rgb(0, 240, 255)); ImGui::PushStyleColor(ImGuiCol_Text, rgb(6, 5, 13)); }
        ImGui::PushID(i);
        if (ImGui::Button(t) && !on) { sel = i; ch = true; }
        ImGui::PopID();
        if (on) ImGui::PopStyleColor(2);
    }
    ImGui::PopID();
    return ch;
}

bool UiApp::slider(const char* title, const char* hint, int& v, int lo, int hi) {
    rowTitle(title, hint);
    ImGui::PushID(title);
    ImGui::SetNextItemWidth(320);
    bool ch = ImGui::SliderInt("##v", &v, lo, hi);
    ImGui::PopID();
    ImGui::Spacing();
    return ch;
}

bool UiApp::toggle(const char* title, const char* hint, bool& v) {
    rowTitle(title, hint);
    ImGui::PushID(title);
    bool ch = false;
    int sel = v ? 1 : 0;
    const char* lab[] = {"Выкл", "Вкл"};
    if (seg("t", lab, 2, sel)) { v = sel == 1; ch = true; }
    ImGui::PopID();
    ImGui::Spacing();
    return ch;
}

void UiApp::changed() {
    preview_.sync(draft_);
    changed_ = true;
    status_ = L("Предпросмотр обновлён. Чтобы это появилось на экране, нажмите «Включить».");
}

static bool beginCard(const char* title) {
    ImGui::PushStyleVar(ImGuiStyleVar_ChildRounding, 4);
    bool ok = ImGui::BeginChild(title, ImVec2(0, 0), ImGuiChildFlags_AutoResizeY | ImGuiChildFlags_Borders | ImGuiChildFlags_AlwaysUseWindowPadding);
    ImGui::PushFont(gMono);
    ImGui::TextUnformatted(L(title));
    ImGui::PopFont();
    ImGui::Spacing();
    return ok;
}
static void endCard() { ImGui::EndChild(); ImGui::PopStyleVar(); ImGui::Spacing(); }

static const char* kSeasonNames[] = {"По календарю", "Зима", "Весна", "Лето", "Осень", "Выключено"};
static const char* kSeasonIds[] = {"auto", "winter", "spring", "summer", "autumn", "off"};
static const char* kHolidayNames[] = {"Нет", "По датам", "Новый год", "Рождество", "Тет", "Хэллоуин", "День влюблённых", "8 Марта", "Масленица", "Пасха", "Свой праздник"};
static const char* kHolidayIds[] = {"off", "auto", "newyear", "xmas", "tet", "halloween", "valentine", "women", "maslenitsa", "easter", "custom"};

static int indexOf(const char* const* ids, int n, const std::string& v) { for (int i = 0; i < n; i++) if (v == ids[i]) return i; return 0; }

void UiApp::frame() {
    static bool first = true;
    if (first) { first = false; preview_.sync(draft_); }
    ImGuiIO& io = ImGui::GetIO();
    ImGui::SetNextWindowPos(ImVec2(0, 0));
    ImGui::SetNextWindowSize(io.DisplaySize);
    ImGui::Begin("##main", nullptr, ImGuiWindowFlags_NoDecoration | ImGuiWindowFlags_NoMove | ImGuiWindowFlags_NoSavedSettings | ImGuiWindowFlags_NoBringToFrontOnFocus);

    ImGui::PushFont(gMono);
    ImGui::PushStyleColor(ImGuiCol_Text, rgb(0, 240, 255));
    ImGui::TextUnformatted("SEASON PLUS");
    ImGui::PopStyleColor(); ImGui::PopFont();
    ImGui::SameLine(ImGui::GetWindowWidth() - 230 * (ImGui::GetStyle().WindowPadding.x / 26.f));
    {
        int lang = gs::locLang();
        const char* lab[] = {"RU", "EN", "DE"};
        if (seg("lang", lab, 3, lang)) gs::locSet(lang, true);
    }
    ImGui::PushStyleColor(ImGuiCol_Text, ImGui::GetStyle().Colors[ImGuiCol_TextDisabled]);
    ImGui::TextWrapped("%s", L("Снег, листья, лепестки и пух падают поверх окон и копятся на их верхнем краю; весной и летом на окнах растёт трава с цветами. Есть праздники с гирляндами, украшениями и салютом. Клики проходят сквозь эффекты, окна не мешают работе."));
    ImGui::PopStyleColor();
    ImGui::Spacing();

    if (beginCard("1. Так будет выглядеть")) {
        ImGui::PushStyleColor(ImGuiCol_Text, ImGui::GetStyle().Colors[ImGuiCol_TextDisabled]);
        ImGui::TextWrapped("%s", L("Это выдуманный рабочий стол: на нём работает то же самое, что появится у вас. Пока не нажали «Включить», на настоящем экране ничего не меняется."));
        ImGui::PopStyleColor();
        ImGui::Image((ImTextureID)(intptr_t)preview_.srv(), ImVec2((float)Preview::kW * io.FontGlobalScale, (float)Preview::kH * io.FontGlobalScale));
    }
    endCard();

    if (beginCard("2. Время года")) {
        int s = indexOf(kSeasonIds, 6, draft_.Season);
        rowTitle("Сезон", "«По календарю» — само меняется зимой, весной, летом и осенью.");
        if (seg("season", kSeasonNames, 6, s)) { draft_.Season = kSeasonIds[s]; changed(); }
        ImGui::Spacing();
        if (slider("Сколько падает", "от редких снежинок до метели", draft_.Intensity, 5, 100)) changed();
        if (slider("Ветер", "минус — влево, плюс — вправо; порывы бывают всегда", draft_.Wind, -100, 100)) changed();
        if (slider("Скорость", "в процентах", draft_.Speed, 50, 200)) changed();
        if (slider("Размер", "снежинок, листьев, лепестков", draft_.Size, 50, 200)) changed();
    }
    endCard();

    if (beginCard("3. Сугробы, листва и трава на окнах")) {
        if (toggle("Копится на окнах", "снег зимой, лепестки весной, пух летом, листья осенью; если окно сдвинуть — осыпается комьями", draft_.Piles)) changed();
        if (slider("Высота", "насколько высокие сугробы и листва", draft_.PileHeight, 10, 100)) changed();
        if (toggle("И на панели задач", "панель задач — тоже подоконник (только если она внизу экрана)", draft_.TaskbarPile)) changed();
    }
    endCard();

    if (beginCard("4. Праздники")) {
        int h = indexOf(kHolidayIds, 11, draft_.Holiday);
        rowTitle("Праздник", "«По датам» — включается сам: Новый год, Хэллоуин, 14 февраля, 8 Марта, Масленица, Пасха. Или выберите вручную.");
        if (seg("holiday", kHolidayNames, 11, h)) { draft_.Holiday = kHolidayIds[h]; changed(); }
        ImGui::Spacing();
        if (toggle("Украшения на окнах", "тыквы, ёлки и подарки, яйца, тюльпаны, блины — сидят на верхнем крае окон", draft_.Props)) changed();
        if (toggle("Отсчёт до Нового года", "в последние 10 секунд года — большие цифры, потом поздравление", draft_.Countdown)) changed();
        if (toggle("Салют в полночь", "в Новый год ровно в 00:00", draft_.NyFireworks)) changed();
        rowTitle("Салют сейчас", "запустить на пробу (в предпросмотре и на экране, если включено)");
        if (ImGui::Button(L("Запустить салют"))) { preview_.engine().startFireworks(10); if (hooks_.fireworks) hooks_.fireworks(); }
        ImGui::Spacing();
        rowTitle("Проверить новогоднюю полночь", "отсчёт 10…1, поздравление и салют — как будет в Новый год");
        if (ImGui::Button(L("Проверить"))) { draft_.Holiday = "newyear"; changed(); preview_.engine().nyDemo = 0; }
        ImGui::Spacing();
        if (draft_.Holiday == "custom") {
            rowTitle("Дата праздника", "каждый год в этот день");
            ImGui::SetNextItemWidth(110); if (ImGui::InputInt(L("день"), &draft_.CustomDay, 0, 0)) { draft_.normalize(); changed(); }
            ImGui::SameLine(); ImGui::SetNextItemWidth(110); if (ImGui::InputInt(L("месяц"), &draft_.CustomMonth, 0, 0)) { draft_.normalize(); changed(); }
            const char* fx[] = {"Шарики", "Конфетти", "Цветы", "Салют"};
            int f = draft_.CustomFx;
            rowTitle("Что падает", "");
            if (seg("fx", fx, 4, f)) { draft_.CustomFx = f; changed(); }
        }
    }
    endCard();

    if (beginCard("5. Гирлянда")) {
        const char* when[] = {"Никогда", "В праздники", "Всегда"};
        const char* where[] = {"На окнах и панели задач", "Только на панели задач"};
        const char* mode[] = {"Переливы", "Бегущие огни", "Мерцание", "Ровный свет", "Вспышки", "Смена цвета"};
        const char* color[] = {"Разноцветная", "Тёплая", "Холодная", "Синяя", "Красно-зелёная", "Золотая"};
        const char* bulb[] = {"Лампочки", "Шарики", "Звёздочки", "Бахрома"};
        rowTitle("Когда", ""); if (seg("g1", when, 3, draft_.Garland)) changed();
        ImGui::Spacing(); rowTitle("Где", ""); if (seg("g2", where, 2, draft_.GarlandWhere)) changed();
        ImGui::Spacing(); rowTitle("Как горит", ""); if (seg("g3", mode, 6, draft_.GarlandMode)) changed();
        ImGui::Spacing(); rowTitle("Цвет", ""); if (seg("g4", color, 6, draft_.GarlandColor)) changed();
        ImGui::Spacing(); rowTitle("Лампочки", ""); if (seg("g5", bulb, 4, draft_.GarlandBulb)) changed();
    }
    endCard();

    if (beginCard("6. Видеокарта")) {
        const char* gpu[] = {"Как решит Windows", "Мощная видеокарта"};
        rowTitle("Чем рисовать", "Эффекты рисуются на видеокарте. Если у вас две (встроенная и мощная) — выберите, какая. «Как решит Windows» подходит почти всегда.");
        if (seg("gpu", gpu, 2, draft_.Gpu)) changed();
    }
    endCard();

    ImGui::PushStyleColor(ImGuiCol_Button, rgb(0, 240, 255)); ImGui::PushStyleColor(ImGuiCol_Text, rgb(6, 5, 13));
    if (ImGui::Button(L(draft_.Active ? "Применить изменения" : "Включить"))) {
        draft_.Active = true; draft_.normalize();
        if (hooks_.apply) hooks_.apply(draft_);
        saved_ = draft_;
        status_ = L("Включено. Season Plus остаётся в фоне и запускается вместе с Windows. Окно можно закрыть.");
    }
    ImGui::PopStyleColor(2);
    ImGui::SameLine();
    if (ImGui::Button(L("Выключить"))) {
        draft_.Active = false;
        if (hooks_.revert) hooks_.revert();
        saved_ = draft_;
        status_ = L("Выключено: с экрана и из автозапуска всё убрано.");
    }
    ImGui::PushStyleColor(ImGuiCol_Text, ImGui::GetStyle().Colors[ImGuiCol_TextDisabled]);
    ImGui::TextWrapped("%s", status_.empty() ? L(draft_.Active ? "Включено: Season Plus работает в фоне и запускается вместе с Windows." : "Не включено.") : status_.c_str());
    ImGui::TextWrapped("%s", L("Полноэкранные игры, видео и презентации плагин определяет сам и в это время ничего не рисует. «Выключить» убирает всё с экрана и из автозапуска."));
    ImGui::PopStyleColor();
    ImGui::End();
}

}  // namespace sp
