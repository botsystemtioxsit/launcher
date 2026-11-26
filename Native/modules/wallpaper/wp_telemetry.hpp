// Данные компьютера для обоев «Терминал»: процессор и память, звук, курсор, журнал событий, погода.
// Страница получает только то, что перечислено в её манифесте, и только пока обои видны. Ничего не пишется на диск и не уходит в сеть
// (кроме запроса погоды к Open-Meteo по названию города). Страница сама ничего не просит: данные только приходят к ней сообщениями.
#pragma once
#include <windows.h>
#include <atomic>
#include <functional>
#include <mutex>
#include <set>
#include <string>
#include <thread>
#include <vector>
#include "wp_model.hpp"

namespace wp {

class AudioTap {
public:
    ~AudioTap() { stop(); }
    void start();
    void stop();
    bool running() const { return run_; }
    std::string snapshot();           // JSON {level,bands[32],vol,muted,active}
    float volume() const { return vol_; }
private:
    static const int kFft = 1024, kBands = 32;
    std::thread th_;
    std::atomic<bool> run_{false};
    std::mutex mu_;
    float ring_[kFft] = {};
    int wpos_ = 0;
    std::atomic<ULONGLONG> lastData_{0};
    std::atomic<int> rate_{48000};
    std::atomic<float> vol_{0};
    std::atomic<bool> muted_{false}, active_{false};
    float bands_[kBands] = {}, level_ = 0;
    void loop();
};

class Telemetry {
public:
    Telemetry() = default;
    ~Telemetry();
    void setConfig(const Settings& s);
    void setWanted(const std::set<std::string>& kinds);          // что сейчас нужно показанным обоям
    // раз в ~40 мс: вызывает send(полное сообщение) для каждого вида, которому пора
    void pump(const std::function<void(const char* kind, const std::string&)>& send);
    std::vector<std::string> resync(const std::set<std::string>& kinds);   // только что загруженной странице — то, что она пропустила
    void stopAll();
    // поиск города (для настроек): запускает запрос; результат забирается takeCity
    void findCity(const std::string& query);
    bool takeCity(std::string& name, double& lat, double& lon, std::string& error);
    bool cityBusy() const { return cityBusy_; }
    static std::string weatherCodeText(int code);

private:
    Settings cfg_;
    std::set<std::string> wanted_;
    AudioTap audio_;
    // сроки
    ULONGLONG nSys_ = 0, nAudio_ = 0, nInput_ = 0, nMedia_ = 0, nFocus_ = 0, nWin_ = 0, nWeather_ = 0, nWSend_ = 0;
    // sys
    ULONGLONG lastIdle_ = 0, lastKernel_ = 0, lastUser_ = 0;
    double cpu_ = 0, ram_ = 0, netDown_ = 0, netUp_ = 0;
    ULONGLONG lastNetDown_ = 0, lastNetUp_ = 0, lastNetTick_ = 0;
    int hotCpu_ = 0; ULONGLONG cpuCool_ = 0, ramCool_ = 0;
    // ввод
    POINT lastPt_{-1, -1}; bool wasDown_ = false; ULONGLONG evUntil_ = 0; std::string ev_ = "IDLE";
    // журнал
    struct JEntry { long long id; long long t; std::string lv, src, msg; };
    std::vector<JEntry> journal_; long long journalId_ = 0, journalSent_ = 0;
    std::string lastFocus_; std::vector<std::pair<DWORD, std::string>> lastWins_; bool winsPrimed_ = false;
    int lastVol_ = -1; ULONGLONG volSince_ = 0;
    bool started_ = false;
    void addJournal(const std::string& lv, const std::string& src, std::string msg);
    // погода
    std::mutex wmu_;
    std::string weatherJson_, weatherKey_;
    std::atomic<bool> weatherBusy_{false}, cityBusy_{false};
    std::string cityName_, cityErr_; double cityLat_ = 0, cityLon_ = 0; bool cityReady_ = false;
    std::vector<std::thread> workers_;
    void refreshWeather(bool force, const void* unused);
    std::string sysJson(ULONGLONG now);
    std::string inputJson(ULONGLONG now);
    std::string journalJson(long long fromId, int limit);
    void watchFocus(); void watchWindows();
};

}  // namespace wp
