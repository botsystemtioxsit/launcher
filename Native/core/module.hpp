// Модуль приложения «Ghost Style»: сезоны, курсор, обои и т. д. У каждого — своя вкладка в окне и (если нужно) слой поверх экрана.
#pragma once
#include <d3d11.h>
#include <functional>
#include <string>
#include "layer.hpp"

namespace sp {

class OverlayHost;

// Что приложение даёт модулю.
struct AppCtx {
    OverlayHost* overlay = nullptr;
    std::function<void()> changed;       // модуль включён/выключен — пересчитать автозапуск и разбудить слои
};

class Module {
public:
    virtual ~Module() = default;
    virtual const char* id() const = 0;
    virtual const char* title() const = 0;                 // по-русски; переводится словарём
    virtual void attach(AppCtx* app) { app_ = app; }
    virtual void init(ID3D11Device* dev) {}                // видеокарта окна настроек (для предпросмотров); может вызываться снова после закрытия окна
    virtual void shutdownUi() {}                           // окно спрятано — отпустить ресурсы предпросмотра
    virtual void tick(double dt) {}                        // кадр окна (предпросмотр)
    virtual void drawPanel() = 0;                          // содержимое вкладки (между NewFrame и Render)
    virtual bool active() const = 0;                       // включён ли (применён)
    virtual bool needsBackground() const { return active(); }   // нужна ли работа приложения в фоне (слой поверх экрана и т. п.)
    virtual void revert() = 0;                             // «Выключить всё»
    virtual Layer* layer() { return nullptr; }
    virtual void start() {}                                // приложение запущено по-настоящему (есть цикл сообщений): можно поднимать окна
    virtual void stop() {}                                 // приложение закрывается
    // служебное для проверок в CI
    virtual int snapshots(const std::wstring& dir, std::string& log) { return 0; }   // снимки сцен модуля в PNG; число записанных
    virtual int selfTest(const std::wstring& dir, std::string& log) { return 0; }  // проверка на настоящей Windows; возвращает число провалов
    virtual void prepareOverlayTest() {}                                           // включить себя «напоказ» для пробы на экране
protected:
    AppCtx* app_ = nullptr;
};

}  // namespace sp
