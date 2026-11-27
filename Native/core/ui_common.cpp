#include "ui_common.hpp"
#include "loc.hpp"

namespace sp::ui {

using gs::L;

static ImFont* gMono = nullptr;
ImFont* mono() { return gMono; }
ImVec4 rgb(int r, int g, int b, float a) { return ImVec4(r / 255.f, g / 255.f, b / 255.f, a); }
ImVec4 muted() { return ImGui::GetStyle().Colors[ImGuiCol_TextDisabled]; }

bool loadFonts(float scale) {
    ImGuiIO& io = ImGui::GetIO();
    io.Fonts->Clear();
    // латиница + кириллица + знаки препинания (кавычки “ ”, многоточие, тире) + стрелки
    static ImVector<ImWchar> ranges;
    if (ranges.empty()) {
        ImFontGlyphRangesBuilder b;
        b.AddRanges(io.Fonts->GetGlyphRangesCyrillic());
        static const ImWchar extra[] = {0x2000, 0x206F, 0x2190, 0x21FF, 0x25A0, 0x25FF, 0};
        b.AddRanges(extra);
        b.BuildRanges(&ranges);
    }
    ImFont* f = io.Fonts->AddFontFromFileTTF("C:\\Windows\\Fonts\\segoeui.ttf", 17.f * scale, nullptr, ranges.Data);
    gMono = io.Fonts->AddFontFromFileTTF("C:\\Windows\\Fonts\\consolab.ttf", 22.f * scale, nullptr, ranges.Data);
    if (!f) io.Fonts->AddFontDefault();
    if (!gMono) gMono = io.Fonts->Fonts[0];
    return f != nullptr;
}

void setupStyle(float scale) {
    ImGuiStyle& s = ImGui::GetStyle();
    s = ImGuiStyle();
    s.WindowRounding = 0; s.ChildRounding = 20; s.FrameRounding = 12; s.GrabRounding = 8; s.PopupRounding = 14; s.ScrollbarRounding = 8;
    s.FrameBorderSize = 1; s.ChildBorderSize = 1; s.WindowBorderSize = 0;
    s.ItemSpacing = ImVec2(8, 8); s.FramePadding = ImVec2(12, 6); s.WindowPadding = ImVec2(26, 22);
    s.ScaleAllSizes(scale);
    ImVec4* c = s.Colors;
    c[ImGuiCol_Text] = rgb(238, 241, 246); c[ImGuiCol_TextDisabled] = rgb(180, 187, 200);
    c[ImGuiCol_WindowBg] = rgb(58, 60, 65); c[ImGuiCol_ChildBg] = rgb(74, 77, 85);
    c[ImGuiCol_Border] = rgb(94, 98, 108);
    c[ImGuiCol_Button] = rgb(112, 116, 128); c[ImGuiCol_ButtonHovered] = rgb(132, 137, 150); c[ImGuiCol_ButtonActive] = rgb(141, 184, 255);
    c[ImGuiCol_FrameBg] = rgb(92, 96, 106); c[ImGuiCol_FrameBgHovered] = rgb(110, 114, 126); c[ImGuiCol_FrameBgActive] = rgb(130, 136, 150);
    c[ImGuiCol_SliderGrab] = rgb(200, 208, 218); c[ImGuiCol_SliderGrabActive] = rgb(141, 184, 255);
    c[ImGuiCol_CheckMark] = rgb(141, 184, 255);
    c[ImGuiCol_ScrollbarBg] = rgb(58, 60, 65); c[ImGuiCol_ScrollbarGrab] = rgb(110, 114, 126);
    c[ImGuiCol_Header] = rgb(92, 96, 106); c[ImGuiCol_HeaderHovered] = rgb(110, 114, 126); c[ImGuiCol_HeaderActive] = rgb(141, 184, 255);
}

void rowTitle(const char* title, const char* hint) {
    ImGui::TextUnformatted(L(title));
    if (hint && *hint) { ImGui::PushStyleColor(ImGuiCol_Text, muted()); ImGui::TextWrapped("%s", L(hint)); ImGui::PopStyleColor(); }
}

void note(const char* ru) { ImGui::PushStyleColor(ImGuiCol_Text, muted()); ImGui::TextWrapped("%s", L(ru)); ImGui::PopStyleColor(); }

bool seg(const char* id, const char* const* labels, int n, int& sel) {
    bool ch = false;
    float avail = ImGui::GetContentRegionAvail().x, x = 0;
    ImGui::PushID(id);
    for (int i = 0; i < n; i++) {
        const char* t = L(labels[i]);
        float w = ImGui::CalcTextSize(t).x + ImGui::GetStyle().FramePadding.x * 2;
        if (i > 0) { if (x + w + 8 > avail) x = 0; else ImGui::SameLine(); }
        x += w + 8;
        bool on = sel == i;
        if (on) { ImGui::PushStyleColor(ImGuiCol_Button, rgb(200, 208, 218)); ImGui::PushStyleColor(ImGuiCol_Text, rgb(6, 5, 13)); }
        ImGui::PushID(i);
        if (ImGui::Button(t) && !on) { sel = i; ch = true; }
        ImGui::PopID();
        if (on) ImGui::PopStyleColor(2);
    }
    ImGui::PopID();
    return ch;
}

bool slider(const char* title, const char* hint, int& v, int lo, int hi) {
    rowTitle(title, hint);
    ImGui::PushID(title);
    ImGui::SetNextItemWidth(320);
    bool ch = ImGui::SliderInt("##v", &v, lo, hi);
    ImGui::PopID();
    ImGui::Spacing();
    return ch;
}

bool toggle(const char* title, const char* hint, bool& v) {
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

bool beginCard(const char* title) {
    bool ok = ImGui::BeginChild(title, ImVec2(0, 0), ImGuiChildFlags_AutoResizeY | ImGuiChildFlags_Borders | ImGuiChildFlags_AlwaysUseWindowPadding);
    ImGui::PushFont(gMono);
    ImGui::TextUnformatted(L(title));
    ImGui::PopFont();
    ImGui::Spacing();
    return ok;
}
void imageFit(ImTextureID tex, float w, float h) { float aw = ImGui::GetContentRegionAvail().x; float k = aw / w; ImGui::Image(tex, ImVec2(aw, h * k)); }
void endCard() { ImGui::EndChild(); ImGui::Spacing(); }

}  // namespace sp::ui
