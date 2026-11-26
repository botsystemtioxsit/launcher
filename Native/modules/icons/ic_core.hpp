// Значки Windows одним цветом: берём исходные значки системы (диски, «Этот компьютер», типы файлов, ярлыки…), перекрашиваем в выбранный цвет
// и записываем так, как Windows хранит значки (реестр пользователя, desktop.ini, ярлыки). Каждое прежнее значение запоминается — «Вернуть как было» возвращает всё.
#pragma once
#include <functional>
#include <memory>
#include <map>
#include <string>
#include <vector>
#include "cursor_files.hpp"

namespace ic {

using Img = sp::CurImg;

struct Settings {
    std::string Color = "#00F0FF", Style = "mono";        // mono — один цвет; hue — сменить оттенок, сохранив краски
    int Strength = 100;                                    // 0–100
    std::vector<std::string> Enabled;
    int Version = 2;
    bool AutoRestart = true;                               // после применения Проводник перезапускается сам (с чисткой кэша значков)
    static Settings load();
    void save() const;
    void normalize();
};

enum class Kind { Folder, Drive, Clsid, FileType, Shortcuts, KnownFolders, AllFileTypes };
struct Target {
    const char* id; const char* group; const char* title; Kind kind;
    std::vector<const char*> exts; const char* clsid = nullptr; const char* valueName = nullptr; bool needsAdmin = false;
};
const std::vector<Target>& targets();
const Target* targetById(const std::string& id);

struct Outcome { std::string group, item, result; bool ok() const { return result == "ok"; } };
struct ApplyResult { int done = 0, failed = 0; std::vector<std::string> messages; std::vector<Outcome> outcomes; };

struct Preview { std::string name; Img before, after; std::string problem; bool hasBefore = false, hasAfter = false; };

// чистая логика (проверяется отдельно)
void tintColor(Img& img, const std::string& hex, const std::string& style, int strength);
bool isBlank(const Img& img);
bool isBlankSet(const std::vector<Img>& sizes);
std::string buildIco(const std::vector<Img>& sizes);
Img resizeImg(const Img& src, int n);
unsigned char* pixelsOf(Img& i);

class Engine {
public:
    Engine();
    ApplyResult applyColor(const Settings& s, const std::function<void(const std::string&)>& progress = nullptr, bool refresh = true);
    int restoreAll(bool refresh = true);
    bool applied() const;
    int changesCount() const;
    std::vector<Preview> previews(const Settings& s);
    void fullRefresh();                                  // перезапуск Проводника с чисткой кэша значков (долго — не из потока окна)
    static void notifyChanged();
private:
    public:
    struct State;
private:
    std::unique_ptr<State> st_;
public:
    ~Engine();
};

// действие с правами администратора (запуск: GhostStyle.exe --icons-machine=<файл>)
int runMachineJob(const std::wstring& jobFile);
bool isAdmin();
std::wstring rootDir();                                  // %APPDATA%\BattleLauncher\icon-plus

}  // namespace ic
