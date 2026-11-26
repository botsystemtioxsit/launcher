// Обои: описание, настройки и библиотека (та же папка и тот же формат manifest.json, что у прежнего плагина Wallpaper Plus).
#pragma once
#include <map>
#include <string>
#include <vector>

namespace wp {

struct Param {
    std::string key, label, type = "range";       // color | range | choice | text | layout
    std::vector<std::string> options;
    double min = 0, max = 1, step = 0.1;
    std::string def;
};

struct Wallpaper {
    std::string id, title, type = "html", entry = "index.html", description, author, source;
    int version = 1;
    bool online = false;                          // обоям разрешён выход в интернет
    std::vector<Param> params;
    std::map<std::string, std::string> values;
    bool tmpl = false, overlay = false;
    std::string background;                       // обои под прозрачным слоем
    std::vector<std::string> telemetry;           // sys, audio, media, input, journal, weather
    std::wstring dir;
    long long sizeBytes = 0;
    bool builtin() const { return id.rfind("b-", 0) == 0; }
    std::string paramQuery() const;               // k=v&k=v для адреса страницы
    const char* typeLabel() const;
};

struct Settings {
    std::string Active;                           // пусто — обои выключены
    bool AllMonitors = true, PauseOnFullscreen = true, PauseWhenCovered = true, Mute = true;
    std::string Fit = "cover";
    int Fps = 60;
    bool Autostart = false, Paused = false, TelAudio = true, TelJournal = true;
    std::string WeatherQuery, WeatherName;
    double WeatherLat = 0, WeatherLon = 0;
    static Settings load();
    void save() const;
    void normalize();
};

struct Result { bool ok = false; std::string id, message; };

// пути
std::wstring dataDir();                           // %LOCALAPPDATA%\GhostShell\WallpaperPlus
std::wstring libraryDir();
std::wstring downloadsDir();
std::wstring systemDir();                         // compose.html для составных обоев
void log(const std::string& text);

// библиотека
std::vector<Wallpaper> listAll();
bool getWallpaper(const std::string& id, Wallpaper& out);
void saveWallpaper(const Wallpaper& w);
void removeWallpaper(const std::string& id);
void ensureBuiltins(const std::string& version);
std::string mediaFile(const Wallpaper& w);        // главный файл у обоев-видео/картинки
std::string readAsset(const char* path);          // файл, вшитый в программу
Result installPath(const std::wstring& path, const std::string& source);                   // файл, папка или zip
Result installDownloaded(const std::wstring& tempDir, const std::string& id);              // из каталога
Result createFrom(const Wallpaper& tmpl, const std::string& title, const std::map<std::string, std::string>& values, const std::string& background);
void updateValues(Wallpaper& w, const std::map<std::string, std::string>& values, const std::string& background);
void ensureSystemFiles();

bool httpGet(const std::string& url, std::string& body, long long maxBytes, std::string& error);   // https, коротко и безопасно
bool httpGetToFile(const std::string& url, const std::wstring& path, long long maxBytes, std::string& error);

// каталог (список лежит в репозитории проекта)
struct CatalogItem { std::string id, title, description, type; std::vector<std::string> files; std::string libraryId() const { return "c-" + id; } };
bool fetchCatalog(std::vector<CatalogItem>& out, std::string& error);
Result installCatalogItem(const CatalogItem& item);

}  // namespace wp
