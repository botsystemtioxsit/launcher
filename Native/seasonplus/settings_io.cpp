#include "model.hpp"
#include "win.hpp"

namespace sp {

static std::wstring settingsPath() { return gs::appDataDir() + L"\\season-plus-settings.json"; }

Settings Settings::load() {
    std::string t; gs::Json j;
    if (gs::readFile(settingsPath(), t) && gs::jsonParse(t, j) && j.t == gs::Json::Obj) return fromJson(j);
    return Settings();
}

void Settings::save() const {
    gs::makeDirs(gs::appDataDir());
    std::string out; gs::jsonDump(toJson(), out);
    gs::writeFileAtomic(settingsPath(), out);
}

}  // namespace sp
