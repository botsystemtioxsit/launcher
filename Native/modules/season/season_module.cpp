#include "season_module.hpp"
#include "imgui.h"
#include "loc.hpp"
#include "overlay_host.hpp"
#include "scenes.hpp"
#include "ui_common.hpp"

namespace sp {

using gs::L;
using namespace ui;

static const char* kSeasonNames[] = {"По календарю", "Зима", "Весна", "Лето", "Осень", "Выключено"};
static const char* kSeasonIds[] = {"auto", "winter", "spring", "summer", "autumn", "off"};
static const char* kHolidayNames[] = {"Нет", "По датам", "Новый год", "Рождество", "Тет", "Хэллоуин", "День влюблённых", "8 Марта", "Масленица", "Пасха", "Свой праздник"};
static const char* kHolidayIds[] = {"off", "auto", "newyear", "xmas", "tet", "halloween", "valentine", "women", "maslenitsa", "easter", "custom"};
static int indexOf(const char* const* ids, int n, const std::string& v) { for (int i = 0; i < n; i++) if (v == ids[i]) return i; return 0; }

SeasonModule::SeasonModule() {
    draft_ = Settings::load();
    saved_ = draft_;
    layer_.setSettings(saved_);
}

void SeasonModule::init(ID3D11Device* dev) {
    if (preview_.init(dev)) preview_.sync(draft_);
}

void SeasonModule::changed() {
    preview_.sync(draft_);
    status_ = L("Предпросмотр обновлён. Чтобы это появилось на экране, нажмите «Включить».");
}

void SeasonModule::applyNow(const Settings& s) {
    saved_ = s; saved_.normalize(); draft_ = saved_;
    saved_.save();
    layer_.setSettings(saved_);
    if (app_ && app_->changed) app_->changed();
    if (preview_.ready()) preview_.sync(draft_);
}

int SeasonModule::snapshots(const std::wstring& dir, std::string& log) { return renderSeasonScenes(dir, log); }

void SeasonModule::prepareOverlayTest() {
    Settings s; s.Season = "winter"; s.Holiday = "off"; s.Intensity = 100; s.Speed = 200; s.Active = true; s.normalize();
    layer_.setSettings(s);
}

void SeasonModule::revert() {
    saved_.Active = false; draft_.Active = false;
    saved_.save();
    layer_.setSettings(saved_);
    if (app_ && app_->changed) app_->changed();
}

void SeasonModule::drawPreview() {
    note("Это выдуманный рабочий стол: на нём работает то же самое, что появится у вас. Пока не нажали «Включить», на настоящем экране ничего не меняется.");
    if (preview_.ready()) imageFit((ImTextureID)(intptr_t)preview_.srv(), (float)SeasonPreview::kW, (float)SeasonPreview::kH);
}

void SeasonModule::drawPanel() {
    ImGui::PushFont(mono());
    ImGui::PushStyleColor(ImGuiCol_Text, rgb(0, 240, 255));
    ImGui::TextUnformatted(L("Сезоны и праздники"));
    ImGui::PopStyleColor(); ImGui::PopFont();
    note("Снег, листья, лепестки и пух падают поверх окон и копятся на их верхнем краю; весной и летом на окнах растёт трава с цветами. Есть праздники с гирляндами, украшениями и салютом. Клики проходят сквозь эффекты, окна не мешают работе.");
    ImGui::Spacing();
    ImGui::PushStyleColor(ImGuiCol_Button, rgb(0, 240, 255)); ImGui::PushStyleColor(ImGuiCol_Text, rgb(6, 5, 13));
    if (ImGui::Button(L(saved_.Active ? "Применить изменения" : "Включить"))) {
        draft_.Active = true; draft_.normalize();
        applyNow(draft_);
        status_ = L("Включено. Приложение остаётся в фоне и запускается вместе с Windows. Окно можно закрыть.");
    }
    ImGui::PopStyleColor(2);
    ImGui::SameLine();
    if (ImGui::Button(L("Выключить"))) { revert(); status_ = L("Выключено: с экрана и из автозапуска всё убрано."); }
    ImGui::PushStyleColor(ImGuiCol_Text, muted());
    ImGui::TextWrapped("%s", status_.empty() ? L(saved_.Active ? "Включено: сезоны работают в фоне." : "Не включено.") : status_.c_str());
    ImGui::PopStyleColor();
    ImGui::Spacing();
    note("Полноэкранные игры, видео и презентации приложение определяет само и в это время ничего не рисует.");
    ImGui::Spacing();

    if (beginCard("1. Время года")) {
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

    if (beginCard("2. Сугробы и листва на окнах")) {
        if (toggle("Копится на окнах", "снег зимой, лепестки весной, пух летом, листья осенью; если окно сдвинуть — осыпается комьями", draft_.Piles)) changed();
        if (slider("Высота", "насколько высокие сугробы и листва", draft_.PileHeight, 10, 100)) changed();
        if (toggle("И на панели задач", "панель задач — тоже подоконник (только если она внизу экрана)", draft_.TaskbarPile)) changed();
    }
    endCard();

    if (beginCard("3. Праздники")) {
        int h = indexOf(kHolidayIds, 11, draft_.Holiday);
        rowTitle("Праздник", "«По датам» — включается сам: Новый год, Хэллоуин, 14 февраля, 8 Марта, Масленица, Пасха. Или выберите вручную.");
        if (seg("holiday", kHolidayNames, 11, h)) { draft_.Holiday = kHolidayIds[h]; changed(); }
        ImGui::Spacing();
        if (toggle("Украшения на окнах", "тыквы, ёлки и подарки, яйца, тюльпаны, блины — сидят на верхнем крае окон", draft_.Props)) changed();
        if (toggle("Отсчёт до Нового года", "в последние 10 секунд года — большие цифры, потом поздравление", draft_.Countdown)) changed();
        if (toggle("Салют в полночь", "в Новый год ровно в 00:00", draft_.NyFireworks)) changed();
        rowTitle("Салют сейчас", "запустить на пробу (в предпросмотре и на экране, если включено)");
        if (ImGui::Button(L("Запустить салют"))) { preview_.engine().startFireworks(10); layer_.fireworks(); }
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

    if (beginCard("4. Гирлянда")) {
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

}

}  // namespace sp
