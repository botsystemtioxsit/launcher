// Ghost Style — приложение-комбайн оформления Windows (нативное, C++): сезоны, курсор, обои, панель задач, цвет, значки, скины.
#include <windows.h>
#include <shellapi.h>
#include <memory>
#include <vector>
#include "app.hpp"
#include "color_module.hpp"
#include "cursor_module.hpp"
#include "icons_module.hpp"
#include "season_module.hpp"
#include "skins_module.hpp"
#include "taskbar_module.hpp"
#include "wallpaper_module.hpp"

namespace gs { extern const char* kStyleLoc; }

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int) {
    int argc = 0;
    wchar_t** argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    for (int i = 1; i < argc; i++) {   // действие с правами администратора (значок папок, ярлыки «для всех»): без окна
        std::wstring a = argv[i];
        if (a.rfind(L"--icons-machine=", 0) == 0) { CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED); return ic::runMachineJob(a.substr(16)); }
    }
    std::vector<std::unique_ptr<sp::Module>> modules;
    modules.push_back(std::make_unique<sp::SeasonModule>());
    modules.push_back(std::make_unique<wp::WallpaperModule>());
    modules.push_back(std::make_unique<tb::TaskbarModule>());
    modules.push_back(std::make_unique<ic::IconsModule>());
    modules.push_back(std::make_unique<sp::CursorModule>());
    modules.push_back(std::make_unique<sp::ColorModule>());
    modules.push_back(std::make_unique<sk::SkinsModule>());
    sp::AppInfo info;
    info.locText = gs::kStyleLoc;
    return sp::runApp(info, modules, argc, argv);
}
