// Общие кирпичики окна настроек (Dear ImGui): шрифты, киберпанк-оформление, кнопки выбора, ползунки, карточки.
#pragma once
#include "imgui.h"

namespace sp::ui {

bool loadFonts(float scale);
void setupStyle(float scale);
ImFont* mono();
ImVec4 rgb(int r, int g, int b, float a = 1.f);
ImVec4 muted();

void rowTitle(const char* title, const char* hint);
bool seg(const char* id, const char* const* labels, int n, int& sel);           // ряд кнопок «выбрать одно»
bool slider(const char* title, const char* hint, int& v, int lo, int hi);
bool toggle(const char* title, const char* hint, bool& v);
bool beginCard(const char* title);
void endCard();
void note(const char* ru);
void imageFit(ImTextureID tex, float w, float h);                               // картинка по ширине доступного места                                                      // приглушённый абзац

}  // namespace sp::ui
