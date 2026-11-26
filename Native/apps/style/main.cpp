// Ghost Style — приложение-комбайн оформления Windows (нативное, C++): сезоны, курсор, обои, панель задач, цвет, значки, скины.
#include <windows.h>
#include <shellapi.h>
#include <memory>
#include <vector>
#include "app.hpp"
#include "color_module.hpp"
#include "cursor_module.hpp"
#include "season_module.hpp"

namespace gs { extern const char* kStyleLoc; }

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int) {
    int argc = 0;
    wchar_t** argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    std::vector<std::unique_ptr<sp::Module>> modules;
    modules.push_back(std::make_unique<sp::SeasonModule>());
    modules.push_back(std::make_unique<sp::CursorModule>());
    modules.push_back(std::make_unique<sp::ColorModule>());
    sp::AppInfo info;
    info.locText = gs::kStyleLoc;
    return sp::runApp(info, modules, argc, argv);
}
