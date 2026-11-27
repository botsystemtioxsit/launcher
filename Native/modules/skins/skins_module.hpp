// Модуль «Скины»: набор настроек остальных модулей (сезоны, панель, цвет, значки, обои, курсор) под одним названием.
// Скин — данные, а не программа: только значения из белого списка, ни путей, ни команд, ни кода. Файл .ghostskin — zip из skin.json и preview.jpg.
#pragma once
#include <wrl/client.h>
#include <d3d11.h>
#include <map>
#include <string>
#include <vector>
#include "module.hpp"

namespace sk {

struct Skin {
    std::string id, name = "Мой скин", author, description;
    long long created = 0;
    std::vector<std::pair<std::string, gs::Json>> parts;      // имя части → настройки
    std::vector<unsigned char> preview;                       // JPEG (может быть пустым)
};

class SkinsModule : public sp::Module {
public:
    SkinsModule() = default;
    const char* id() const override { return "skins"; }
    const char* title() const override { return "Скины"; }
    void init(ID3D11Device* dev) override { dev_ = dev; reload_ = true; }
    void shutdownUi() override { tex_.clear(); dev_ = nullptr; }
    void drawPanel() override;
    bool active() const override { return false; }
    void revert() override {}
    int selfTest(const std::wstring& dir, std::string& log) override;

    // чистая логика
    static bool parse(const std::string& text, Skin& out, std::vector<std::string>& warnings, std::string& error);
    static std::string toJson(const Skin& s);
    static std::string makeArchive(const Skin& s);
    static bool readArchive(const std::wstring& file, Skin& out, std::string& error);
    static std::string describe(const std::string& part, const gs::Json& p);
private:
    std::vector<Skin> list_; bool reload_ = true;
    std::vector<Microsoft::WRL::ComPtr<ID3D11ShaderResourceView>> tex_;
    ID3D11Device* dev_ = nullptr;
    std::string status_, confirmId_;
    char name_[64] = "Мой скин", author_[64] = "", desc_[200] = "";
    std::map<std::string, bool> pick_;
    std::vector<unsigned char> pendingPreview_;
    Microsoft::WRL::ComPtr<ID3D11ShaderResourceView> pendingTex_;
    void load();
    std::string save(const Skin& s);
    std::string applySkin(const Skin& s);
    Skin build();
};

}  // namespace sk
