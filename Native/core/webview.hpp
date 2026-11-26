// Встроенный браузер (Microsoft Edge WebView2): страницы, видео и GIF для обоев. Движок — часть Windows 11 (на Windows 10 ставится отдельно).
// Загрузчик WebView2Loader.dll вшит в exe и при первом запуске раскладывается в папку программы.
#pragma once
#include <windows.h>
#include <functional>
#include <memory>
#include <string>

namespace sp {

class WebView {
public:
    WebView();
    ~WebView();
    static bool available();                                         // есть ли движок (Edge WebView2 Runtime)
    // Создаёт браузер в окне host. Вызывать из потока с циклом сообщений. ready(true) — можно навигировать.
    void create(HWND host, const std::wstring& userDataDir, std::function<void(bool)> ready);
    bool isReady() const;
    void navigate(const std::wstring& url);
    void mapFolder(const std::wstring& hostName, const std::wstring& folder);   // https://hostName/ → папка на диске (страница видит только её)
    void resize(int w, int h);
    void setVisible(bool v);
    void setMuted(bool m);
    void postJson(const std::string& utf8);                            // сообщение странице (window.chrome.webview.addEventListener('message'))
    void blockNetwork(bool block, const std::wstring& allowedHost);    // запретить всё, кроме виртуального хоста
    void setOnMessage(std::function<void(const std::string&)> f);
    void suspend(bool s);                                              // «заморозить» страницу, когда обои не видны
    void addInitScript(const std::wstring& js, std::function<void()> done);   // скрипт для каждой страницы (до её скриптов)
    void setOnNavigated(std::function<void()> f);                      // страница загрузилась
    void restrictTo(const std::wstring& hostName);                       // переходить можно только на этот хост и его поддомены
    void execute(const std::wstring& js, std::function<void(const std::string&)> done);   // выполнить скрипт, вернуть результат (JSON) — для проверок
    void close();
    std::wstring lastError() const;
private:
    struct Impl;
    std::unique_ptr<Impl> p_;
};

}  // namespace sp
