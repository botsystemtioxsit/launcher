// Модуль «Обои»: живые обои рабочего стола (видео, GIF, картинки, страницы, архивы), каталог, конструктор, «Терминал».
#pragma once
#include <atomic>
#include <memory>
#include <mutex>
#include <thread>
#include "module.hpp"
#include "wp_controller.hpp"
#include "wp_model.hpp"

namespace wp {

class EditorWindow;

class WallpaperModule : public sp::Module {
public:
    WallpaperModule();
    ~WallpaperModule() override;
    const char* id() const override { return "wallpaper"; }
    const char* title() const override { return "Обои"; }
    void start() override;
    void stop() override;
    void drawPanel() override;
    bool active() const override { return !saved_.Active.empty(); }
    const char* skinPart() const override { return "wallpaper"; }
    bool skinCapture(gs::Json& out) override;
    std::string skinApply(const gs::Json& part) override;
    void revert() override;
    int selfTest(const std::wstring& dir, std::string& log) override;
    Controller& controller() { return ctl_; }

private:
    Settings saved_;
    Controller ctl_;
    std::vector<Wallpaper> list_;
    bool listDirty_ = true;
    std::string status_, importMsg_;
    std::string editId_;
    std::map<std::string, std::string> edit_;
    std::string editBg_;
    double liveAt_ = 0;
    char editTitle_[96] = {};
    std::string confirmDelete_;
    // каталог
    std::vector<CatalogItem> catalog_;
    std::string catalogErr_, catalogMsg_;
    std::atomic<bool> catalogBusy_{false}, catalogLoaded_{false};
    std::mutex mu_;
    std::vector<std::thread> jobs_;
    std::vector<std::string> pendingMsgs_;
    // настройки
    char city_[96] = {};
    // редактор панелей
    std::unique_ptr<EditorWindow> editor_;

    void refreshList();
    void applySettings();
    void activate(const std::string& id);
    void startEdit(const std::string& id);
    void tabLibrary(); void tabCatalog(); void tabCreate(); void tabSettings();
    void importFrom(const std::wstring& path);
    void openEditor();
    const Wallpaper* find(const std::string& id) const;
    std::string val(const Param& p) const;
};

}  // namespace wp
