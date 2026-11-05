// «Стилист» движка 7. Всё, что трогает объекты XAML, выполняется только в потоке XAML (там, где панель их создала);
// из других потоков вызываются лишь SetConfig/WriteTree/GetStats, которые XAML-объектов не касаются.
//
// Что делает (и больше ничего):
//  • пока движок включён, родной фон панели (Rectangle#BackgroundFill) прозрачный, а тонкая линия (Rectangle#BackgroundStroke) скрыта;
//    при выключении исходный вид возвращается. Плашки и острова рисует сам плагин в своём окне под панелью;
//  • измеряет, где на панели лежат значки приложений, виджеты, часы с треем, и раз в сотню миллисекунд (когда что-то сдвинулось)
//    записывает пиксели относительно окна панели в файл taskbar-plus-geometry.txt.
//
// Почему не двигаем и не рисуем внутри XAML: у разных сборок Windows разное дерево панели (на 26300 системная область — не внутри
// TaskbarFrame, а рядом с ним), и любые расчёты «внутри XAML» ломались на этом. Измерение же одинаково везде: TransformToVisual(корень).
#include "styler.h"

#undef GetCurrentTime
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.UI.h>
#include <winrt/Windows.UI.Xaml.h>
#include <winrt/Windows.UI.Xaml.Controls.h>
#include <winrt/Windows.UI.Xaml.Media.h>
#include <winrt/Windows.UI.Xaml.Shapes.h>

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <map>
#include <mutex>
#include <set>
#include <string>
#include <unordered_map>
#include <vector>

namespace wf = winrt::Windows::Foundation;
namespace xaml = winrt::Windows::UI::Xaml;
namespace ctl = winrt::Windows::UI::Xaml::Controls;
namespace med = winrt::Windows::UI::Xaml::Media;
namespace shp = winrt::Windows::UI::Xaml::Shapes;

namespace
{
    enum class Kind { BgFill = 1, BgStroke = 2, App = 3, Widgets = 4, System = 5, Frame = 8 };

    struct Item
    {
        Kind kind = Kind::App;
        std::wstring type, name;
        wf::IInspectable obj{ nullptr };

        // исходный вид (для фона и обводки)
        bool saved = false;
        med::Brush origFill{ nullptr };
        double origOpacity = 1;
        med::Brush ourFill{ nullptr };   // кисть, которую поставили мы (прозрачная); пусто — фон не тронут
        bool strokeHidden = false;
    };

    struct Node
    {
        InstanceHandle handle = 0;
        uint64_t parent = 0;
        uint32_t index = 0;
        uint32_t order = 0;
        std::wstring type, name;
    };

    struct Box
    {
        int l = 0, t = 0, r = 0, b = 0;
        bool ok = false;
        void Grow(int L, int T, int R, int B)
        {
            if (!ok) { l = L; t = T; r = R; b = B; ok = true; return; }
            l = std::min(l, L); t = std::min(t, T); r = std::max(r, R); b = std::max(b, B);
        }
    };

    // Одна панель задач (одно окно XAML со своим TaskbarFrame): её размер в пикселях и положение групп внутри неё.
    struct GeoBar
    {
        int rootW = 0, rootH = 0, scale100 = 100;
        Box widgets, system, apps;
        std::vector<Box> icons;
    };

    struct TS
    {
        IXamlDiagnostics* diag = nullptr;
        std::unordered_map<uint64_t, Item> items;
        xaml::DispatcherTimer timer{ nullptr };
        int tick = 0;
        int appliedGen = -1;
        std::vector<Rule> rules;     // дополнительные правила из настроек
        std::string rulesSig = "?";
        // счётчики читает поток наблюдателя
        std::atomic<int> nBgFill{ 0 }, nBgStroke{ 0 }, nApps{ 0 }, nWidgets{ 0 }, nSystem{ 0 }, nBars{ 0 }, nGeoBars{ 0 }, nTouched{ 0 };
    };

    std::mutex g_registryLock;
    std::vector<TS*> g_registry;
    thread_local TS* t_state = nullptr;

    TS& T()
    {
        if (!t_state)
        {
            t_state = new TS();
            std::lock_guard<std::mutex> l(g_registryLock);
            g_registry.push_back(t_state);
        }
        return *t_state;
    }

    // ----- общее для потоков -----
    std::mutex g_cfgLock;
    Config g_cfg;
    std::mutex g_treeLock;
    std::unordered_map<uint64_t, Node> g_nodes;
    uint32_t g_order = 0;
    std::atomic<int> g_nRects{ 0 };
    std::atomic<bool> g_queueOk{ false };
    std::atomic<int> g_cfgGen{ 0 };               // растёт при каждой смене настроек; каждый поток XAML замечает это на своём тике
    std::mutex g_noteLock;
    std::wstring g_rectNames;     // имена прямоугольников, увиденных на панели (для отчёта, если фон не нашёлся)
    std::mutex g_geoLock;
    std::map<TS*, std::vector<GeoBar>> g_geo;     // последние измерения каждого потока
    std::string g_geoBody;                        // что записано в файл в прошлый раз (без метки времени)
    ULONGLONG g_geoWrittenAt = 0;

    uint64_t Key(InstanceHandle h) { return (uint64_t)h; }
    Config CurrentConfig() { std::lock_guard<std::mutex> l(g_cfgLock); return g_cfg; }

    // ---------- правила: что за элемент ----------
    bool Glob(const std::string& pat, const std::wstring& text)
    {
        if (pat.empty()) return true;
        std::wstring p(pat.begin(), pat.end());
        size_t pi = 0, ti = 0, star = std::wstring::npos, mark = 0;
        while (ti < text.size())
        {
            if (pi < p.size() && p[pi] == L'*') { star = pi++; mark = ti; }
            else if (pi < p.size() && p[pi] == text[ti]) { ++pi; ++ti; }
            else if (star != std::wstring::npos) { pi = star + 1; ti = ++mark; }
            else return false;
        }
        while (pi < p.size() && p[pi] == L'*') ++pi;
        return pi == p.size();
    }

    // Встроенные правила — по дереву панели с настоящего компьютера (Windows 11 26100 и 26300, см. CLAUDE.md, «Taskbar Plus 3»).
    const std::vector<Rule>& BuiltinRules()
    {
        static const std::vector<Rule> rules = {
            { 1, "Windows.UI.Xaml.Shapes.Rectangle", "BackgroundFill" },
            { 2, "Windows.UI.Xaml.Shapes.Rectangle", "BackgroundStroke" },
            { 3, "Taskbar.TaskListButton", "" },
            { 3, "Taskbar.ExperienceToggleButton", "" },
            { 3, "Taskbar.SearchBoxButton", "" },
            { 3, "SearchUx.SearchUI.SearchBoxButton", "" },
            { 4, "Taskbar.AugmentedEntryPointButton", "" },
            { 5, "SystemTray.*", "" },
            { 8, "Taskbar.TaskbarFrame", "" },
        };
        return rules;
    }

    int Classify(const std::wstring& type, const std::wstring& name)
    {
        for (auto& r : BuiltinRules())
            if (Glob(r.type, type) && Glob(r.name, name)) return r.kind;
        for (auto& r : T().rules)
            if (Glob(r.type, type) && Glob(r.name, name)) return r.kind;
        return 0;
    }

    void SyncRules(const Config& c)
    {
        if (T().rulesSig == c.rulesSig) return;
        T().rulesSig = c.rulesSig;
        T().rules = c.rules;
    }

    // ---------- чтение и запись «поверхности» элемента (прямоугольник, рамка, сетка, кнопка) ----------
    med::Brush GetFill(const wf::IInspectable& o)
    {
        if (auto s = o.try_as<shp::Shape>()) return s.Fill();
        if (auto b = o.try_as<ctl::Border>()) return b.Background();
        if (auto p = o.try_as<ctl::Panel>()) return p.Background();
        if (auto c = o.try_as<ctl::Control>()) return c.Background();
        return nullptr;
    }

    void SetFill(const wf::IInspectable& o, const med::Brush& br)
    {
        if (auto s = o.try_as<shp::Shape>()) s.Fill(br);
        else if (auto b = o.try_as<ctl::Border>()) b.Background(br);
        else if (auto p = o.try_as<ctl::Panel>()) p.Background(br);
        else if (auto c = o.try_as<ctl::Control>()) c.Background(br);
    }

    med::Brush ClearBrush() { return med::Brush(med::SolidColorBrush(winrt::Windows::UI::Color{ 0, 0, 0, 0 })); }

    // Корень XAML (окно панели): по нему отличаем панель от всплывающего окна скрытых значков.
    void* RootKey(const xaml::FrameworkElement& fe)
    {
        try
        {
            auto xr = fe.XamlRoot();
            return xr ? winrt::get_abi(xr) : nullptr;
        }
        catch (...) { return nullptr; }
    }

    // Корни, в которых есть TaskbarFrame, — это и есть панели задач (основная и на других мониторах).
    std::set<void*> BarRoots()
    {
        std::set<void*> roots;
        for (auto& kv : T().items)
        {
            if (kv.second.kind != Kind::Frame) continue;
            auto fe = kv.second.obj.try_as<xaml::FrameworkElement>();
            if (!fe) continue;
            if (auto k = RootKey(fe)) roots.insert(k);
        }
        return roots;
    }

    void RecountItems()
    {
        int bg = 0, st = 0, ap = 0, wi = 0, sy = 0, tc = 0;
        for (auto& kv : T().items)
        {
            switch (kv.second.kind)
            {
                case Kind::BgFill: ++bg; if (kv.second.ourFill) ++tc; break;
                case Kind::BgStroke: ++st; if (kv.second.strokeHidden) ++tc; break;
                case Kind::App: ++ap; break;
                case Kind::Widgets: ++wi; break;
                case Kind::System: ++sy; break;
                case Kind::Frame: break;
            }
        }
        T().nBgFill = bg; T().nBgStroke = st; T().nApps = ap; T().nWidgets = wi; T().nSystem = sy; T().nTouched = tc;
    }

    // ---------- фон и обводка панели ----------
    // enabled && isBar — фон прозрачный, обводка скрыта; иначе возвращаем исходный вид (если мы его меняли).
    void ApplyBar(Item& it, const Config& c, bool isBar)
    {
        auto fe = it.obj.try_as<xaml::FrameworkElement>();
        if (!fe) return;
        const bool stroke = it.kind == Kind::BgStroke;

        if (!it.saved)
        {
            it.origFill = GetFill(it.obj);
            it.origOpacity = fe.Opacity();
            it.saved = true;
        }

        const bool on = c.enabled && isBar;
        if (!on)
        {
            if (stroke) { if (it.strokeHidden) { fe.Opacity(it.origOpacity); it.strokeHidden = false; } }
            else if (it.ourFill) { SetFill(it.obj, it.origFill); it.ourFill = nullptr; }
            return;
        }

        if (stroke)
        {
            if (c.hideStroke) { fe.Opacity(0.0); it.strokeHidden = true; }
            else if (it.strokeHidden) { fe.Opacity(it.origOpacity); it.strokeHidden = false; }
            return;
        }
        auto clear = ClearBrush();
        SetFill(it.obj, clear);
        it.ourFill = clear;
    }

    void ApplyBars(const Config& c)
    {
        const auto bars = BarRoots();
        for (auto& kv : T().items)
        {
            auto& it = kv.second;
            if (it.kind != Kind::BgFill && it.kind != Kind::BgStroke) continue;
            auto fe = it.obj.try_as<xaml::FrameworkElement>();
            if (!fe) continue;
            try { ApplyBar(it, c, bars.count(RootKey(fe)) > 0); }
            catch (winrt::hresult_error const& e) { Log(L"применение: ошибка 0x%08X", (unsigned)e.code().value); }
        }
        RecountItems();
    }

    // ---------- измерение ----------
    void FlushGeometry()
    {
        std::string body;
        int n = 0;
        {
            std::lock_guard<std::mutex> l(g_geoLock);
            for (auto& kv : g_geo)
                for (auto& b : kv.second)
                {
                    if (b.rootW <= 0 || b.rootH <= 0) continue;
                    char buf[256];
                    snprintf(buf, sizeof(buf), "bar|%d|%d|%d|%d\n", n, b.rootW, b.rootH, b.scale100);
                    body += buf;
                    auto put = [&](const char* tag, const Box& x, const char* extra)
                    {
                        if (!x.ok) return;
                        snprintf(buf, sizeof(buf), "%s|%d|%d|%d|%d|%d%s\n", tag, n, x.l, x.t, x.r, x.b, extra);
                        body += buf;
                    };
                    put("w", b.widgets, "");
                    put("s", b.system, "");
                    put("a", b.apps, "");
                    for (auto& i : b.icons) put("i", i, "");
                    ++n;
                }
        }
        const auto now = GetTickCount64();
        std::lock_guard<std::mutex> l(g_geoLock);
        if (body == g_geoBody && now - g_geoWrittenAt < 1000) return;   // без изменений — раз в секунду всё равно обновляем метку времени («движок жив»)
        g_geoBody = body;
        g_geoWrittenAt = now;
        char head[160];
        snprintf(head, sizeof(head), "v=1\npid=%lu\ntick=%llu\nbars=%d\n", GetCurrentProcessId(), (unsigned long long)now, n);
        WriteTextAtomic(DataDir() + L"\\taskbar-plus-geometry.txt", std::string(head) + body);
    }

    // Размеры и положение каждой группы в пикселях окна панели (координаты относительно корня XAML × масштаб экрана).
    void Measure()
    {
        std::map<void*, GeoBar> bars;
        for (auto& kv : T().items)
        {
            if (kv.second.kind != Kind::Frame) continue;
            auto fe = kv.second.obj.try_as<xaml::FrameworkElement>();
            if (!fe) continue;
            auto key = RootKey(fe);
            if (!key || bars.count(key)) continue;
            GeoBar g;
            try
            {
                auto xr = fe.XamlRoot();
                const double sc = xr.RasterizationScale();
                const auto sz = xr.Size();
                g.scale100 = (int)std::lround(sc * 100.0);
                g.rootW = (int)std::lround(sz.Width * sc);
                g.rootH = (int)std::lround(sz.Height * sc);
            }
            catch (...) { continue; }
            bars[key] = std::move(g);
        }

        std::map<void*, std::pair<Box, Box>> sys;   // по панелям: (кадр системной области, все её элементы)
        for (auto& kv : T().items)
        {
            auto& it = kv.second;
            if (it.kind != Kind::App && it.kind != Kind::Widgets && it.kind != Kind::System) continue;
            auto fe = it.obj.try_as<xaml::FrameworkElement>();
            if (!fe) continue;
            try
            {
                auto key = RootKey(fe);
                auto bar = bars.find(key);
                if (bar == bars.end()) continue;
                if (fe.Visibility() == xaml::Visibility::Collapsed) continue;
                const double w = fe.ActualWidth(), h = fe.ActualHeight();
                if (w < 2 || h < 2) continue;
                const double sc = bar->second.scale100 / 100.0;
                auto r = fe.TransformToVisual(nullptr).TransformBounds(wf::Rect{ 0, 0, (float)w, (float)h });
                const int l = (int)std::lround(r.X * sc), t = (int)std::lround(r.Y * sc);
                const int rr = (int)std::lround((r.X + r.Width) * sc), bb = (int)std::lround((r.Y + r.Height) * sc);
                if (rr <= 0 || l >= bar->second.rootW || rr - l < 2) continue;   // за краем окна (спрятан или ещё не разложен)
                if (it.kind == Kind::App) { bar->second.apps.Grow(l, t, rr, bb); Box one; one.Grow(l, t, rr, bb); bar->second.icons.push_back(one); }
                else if (it.kind == Kind::Widgets) bar->second.widgets.Grow(l, t, rr, bb);
                else
                {
                    auto& pr = sys[key];
                    pr.second.Grow(l, t, rr, bb);
                    if (it.type == L"SystemTray.SystemTrayFrame") pr.first.Grow(l, t, rr, bb);
                }
            }
            catch (...) { /* элемент как раз пересобирается — в следующий раз */ }
        }
        for (auto& kv : bars)
        {
            auto s = sys.find(kv.first);
            if (s != sys.end()) kv.second.system = s->second.first.ok ? s->second.first : s->second.second;
        }

        std::vector<GeoBar> list;
        int withApps = 0;
        for (auto& kv : bars) { if (kv.second.apps.ok) ++withApps; list.push_back(std::move(kv.second)); }
        T().nBars = (int)list.size();
        T().nGeoBars = withApps;
        { std::lock_guard<std::mutex> l(g_geoLock); g_geo[&T()] = std::move(list); }
        FlushGeometry();
    }

    void ClearGeometry()
    {
        bool had;
        { std::lock_guard<std::mutex> l(g_geoLock); had = g_geo.count(&T()) > 0 && !g_geo[&T()].empty(); g_geo[&T()].clear(); }
        T().nBars = 0; T().nGeoBars = 0;
        if (had) FlushGeometry();
    }

    // ---------- применение ко всему ----------
    void ApplyAll()
    {
        try
        {
            auto c = CurrentConfig();
            SyncRules(c);
            ApplyBars(c);
            if (!c.enabled) ClearGeometry();
        }
        catch (...) { Log(L"ApplyAll: неожиданная ошибка"); }
    }

    // Раз в 100 мс: измерение; раз в секунду проверяем, что Windows не вернула свою кисть.
    void OnTick()
    {
        try
        {
            if (g_cfgGen.load() != T().appliedGen)
            {
                T().appliedGen = g_cfgGen.load();
                ApplyAll();
            }
            auto c = CurrentConfig();
            if (!c.enabled) return;
            if (++T().tick % 10 == 0)
            {
                bool lost = false;
                for (auto& kv : T().items)
                {
                    auto& it = kv.second;
                    if (it.kind == Kind::BgFill && it.ourFill && GetFill(it.obj) != it.ourFill) lost = true;
                }
                if (lost || T().nTouched == 0) ApplyBars(c);
            }
            Measure();
        }
        catch (...) { /* следующий тик */ }
    }

    void EnsureTimer()
    {
        if (T().timer) return;
        try
        {
            T().timer = xaml::DispatcherTimer();
            T().timer.Interval(std::chrono::milliseconds(100));
            T().timer.Tick([](auto&&, auto&&) { OnTick(); });
            T().timer.Start();
            g_queueOk = true;
        }
        catch (winrt::hresult_error const& e) { Log(L"таймер XAML: ошибка 0x%08X", (unsigned)e.code().value); }
    }

    struct BstrGuard
    {
        BSTR& s;
        explicit BstrGuard(BSTR& v) : s(v) {}
        ~BstrGuard() { if (s) { SysFreeString(s); s = nullptr; } }
    };
}

namespace Styler
{
    HRESULT OnChange(IXamlDiagnostics* diag, const ParentChildRelation& relation, const VisualElement& elementIn, VisualMutationType mutation)
    {
        // Каждый поток XAML запоминает «свою» диагностику: элементы можно получить по номеру только через неё.
        if (diag && T().diag != diag) { diag->AddRef(); T().diag = diag; }
        // Строки в VisualElement отдаются нам во владение — освобождаем их сами.
        VisualElement element = elementIn;
        BstrGuard gFile(element.SrcInfo.FileName), gHash(element.SrcInfo.Hash), gName(element.Name), gType(element.Type);
        try
        {
            const uint64_t key = Key(element.Handle);
            if (mutation == Remove)
            {
                { std::lock_guard<std::mutex> l(g_treeLock); g_nodes.erase(key); }
                auto it = T().items.find(key);
                if (it != T().items.end()) { T().items.erase(it); RecountItems(); }
                return S_OK;
            }

            const std::wstring type = element.Type ? element.Type : L"";
            const std::wstring name = element.Name ? element.Name : L"";
            {
                std::lock_guard<std::mutex> l(g_treeLock);
                if (g_nodes.size() < 30000)
                {
                    Node n;
                    n.handle = element.Handle;
                    n.parent = Key(relation.Parent);
                    n.index = relation.ChildIndex;
                    n.order = g_order++;
                    n.type = type;
                    n.name = name;
                    g_nodes[key] = std::move(n);
                }
            }

            EnsureTimer();

            if (type == L"Windows.UI.Xaml.Shapes.Rectangle")
            {
                ++g_nRects;
                if (!name.empty())
                {
                    std::lock_guard<std::mutex> l(g_noteLock);
                    if (g_rectNames.size() < 300 && g_rectNames.find(L"[" + name + L"]") == std::wstring::npos) g_rectNames += L"[" + name + L"]";
                }
            }

            auto c = CurrentConfig();
            SyncRules(c);
            const int kind = Classify(type, name);
            if (kind != 0 && T().diag)
            {
                IInspectable* raw = nullptr;
                if (SUCCEEDED(T().diag->GetIInspectableFromHandle(element.Handle, &raw)) && raw)
                {
                    wf::IInspectable obj{ raw, winrt::take_ownership_from_abi };
                    Item it;
                    it.kind = (Kind)kind;
                    it.type = type;
                    it.name = name;
                    it.obj = obj;
                    T().items[key] = std::move(it);
                    auto& added = T().items[key];
                    // Исходный вид фона запоминаем сразу, как только он появился: до того, как мы его хоть раз тронули.
                    if (added.kind == Kind::BgFill || added.kind == Kind::BgStroke) ApplyBar(added, c, false);
                    RecountItems();
                }
            }
        }
        catch (winrt::hresult_error const& e) { Log(L"OnChange: ошибка 0x%08X", (unsigned)e.code().value); }
        catch (...) { Log(L"OnChange: неожиданная ошибка"); }
        return S_OK; // ошибка здесь не должна ломать панель
    }

    void SetConfig(const Config& cfg)
    {
        { std::lock_guard<std::mutex> l(g_cfgLock); g_cfg = cfg; }
        ++g_cfgGen; // применит поток XAML на ближайшем тике (раз в 100 мс)
    }

    void WriteTree()
    {
        try
        {
            std::lock_guard<std::mutex> l(g_treeLock);
            std::map<uint64_t, std::vector<std::pair<uint32_t, uint64_t>>> kids;
            std::vector<uint64_t> roots;
            for (auto& kv : g_nodes)
            {
                if (kv.second.parent != 0 && g_nodes.count(kv.second.parent)) kids[kv.second.parent].push_back({ kv.second.order, kv.first });
                else roots.push_back(kv.first);
            }
            std::sort(roots.begin(), roots.end(), [](uint64_t a, uint64_t b) { return g_nodes[a].order < g_nodes[b].order; });
            std::wstring out = L"Дерево элементов панели задач (Taskbar Plus, движок " GS_ENGINE_VERSION L")\r\n";
            std::vector<std::pair<uint64_t, int>> stack;
            for (auto it = roots.rbegin(); it != roots.rend(); ++it) stack.push_back({ *it, 0 });
            size_t lines = 0;
            while (!stack.empty() && lines++ < 30000)
            {
                auto [h, depth] = stack.back();
                stack.pop_back();
                auto& n = g_nodes[h];
                out.append((size_t)depth * 2, L' ');
                out += n.type;
                if (!n.name.empty()) { out += L" #"; out += n.name; }
                out += L"\r\n";
                auto k = kids.find(h);
                if (k != kids.end())
                {
                    auto& v = k->second;
                    std::sort(v.begin(), v.end());
                    for (auto it = v.rbegin(); it != v.rend(); ++it) stack.push_back({ it->second, depth + 1 });
                }
            }
            WriteTextAtomic(DataDir() + L"\\taskbar-plus-xaml-tree.txt", ToUtf8(out));
        }
        catch (...) { Log(L"WriteTree: ошибка"); }
    }

    StylerStats GetStats()
    {
        StylerStats s;
        { std::lock_guard<std::mutex> l(g_treeLock); s.nodes = (int)g_nodes.size(); }
        {
            std::lock_guard<std::mutex> l(g_registryLock);
            s.threads = (int)g_registry.size();
            for (auto* t : g_registry)
            {
                s.bgFill += t->nBgFill; s.bgStroke += t->nBgStroke; s.apps += t->nApps; s.widgets += t->nWidgets; s.system += t->nSystem;
                s.bars += t->nBars; s.geoBars += t->nGeoBars; s.touched += t->nTouched;
            }
        }
        s.rects = g_nRects;
        s.queueOk = g_queueOk;
        { std::lock_guard<std::mutex> l(g_noteLock); s.rectNames = g_rectNames; }
        return s;
    }
}
