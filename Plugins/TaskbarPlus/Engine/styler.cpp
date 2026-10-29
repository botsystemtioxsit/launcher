// Сам «стилист»: находит на панели задач Windows 11 нужные элементы XAML и меняет их внешний вид.
// Всё, что трогает объекты XAML, выполняется только в потоке XAML (там, где панель их создала):
// из других потоков вызывается лишь постановка задачи в очередь этого потока.
#include "styler.h"

#undef GetCurrentTime
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.System.h>
#include <winrt/Windows.UI.h>
#include <winrt/Windows.UI.Core.h>
#include <winrt/Windows.UI.Xaml.h>
#include <winrt/Windows.UI.Xaml.Controls.h>
#include <winrt/Windows.UI.Xaml.Media.h>
#include <winrt/Windows.UI.Xaml.Shapes.h>

#include <algorithm>
#include <atomic>
#include <map>
#include <mutex>
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
    enum class Kind { BgFill, BgStroke, Member, Edge };

    struct Item
    {
        Kind kind = Kind::Member;
        wf::IInspectable obj{ nullptr };
        bool saved = false;
        med::Brush origFill{ nullptr };
        xaml::Thickness origMargin{};
        double origRx = 0, origRy = 0, origOpacity = 1;
        med::Brush ourFill{ nullptr };
    };

    struct Host
    {
        ctl::Panel panel{ nullptr };
        shp::Rectangle fill{ nullptr };
        std::vector<ctl::Border> borders;
    };

    struct Node
    {
        uint64_t parent = 0;
        uint32_t index = 0;
        uint32_t order = 0;
        std::wstring type, name;
    };

    // ----- состояние (поток XAML, кроме отмеченного) -----
    IXamlDiagnostics* g_diag = nullptr;
    std::unordered_map<uint64_t, Item> g_items;
    std::vector<Host> g_hosts;
    winrt::Windows::System::DispatcherQueue g_queue{ nullptr };
    winrt::Windows::System::DispatcherQueueTimer g_timer{ nullptr };
    int g_tick = 0;
    std::wstring g_islandSig;
    med::Brush g_barBrush{ nullptr };
    med::Brush g_islandBrush{ nullptr };
    std::wstring g_brushSig;

    // ----- общее для потоков -----
    std::mutex g_cfgLock;
    Config g_cfg;
    std::mutex g_treeLock;
    std::unordered_map<uint64_t, Node> g_nodes;
    uint32_t g_order = 0;
    std::atomic<int> g_nBgFill{ 0 }, g_nBgStroke{ 0 }, g_nMembers{ 0 }, g_nEdges{ 0 }, g_nIslands{ 0 };
    std::atomic<bool> g_applied{ false }, g_queueOk{ false };

    uint64_t Key(InstanceHandle h) { return (uint64_t)h; }

    Config CurrentConfig() { std::lock_guard<std::mutex> l(g_cfgLock); return g_cfg; }

    bool IsMemberType(const std::wstring& t)
    {
        return t == L"Taskbar.TaskListButton" || t == L"Taskbar.ExperienceToggleButton" || t == L"Taskbar.SearchBoxButton";
    }
    bool IsEdgeType(const std::wstring& t)
    {
        return t == L"SystemTray.SystemTrayFrame" || t == L"Taskbar.AugmentedEntryPointButton";
    }

    winrt::Windows::UI::Color Rgba(unsigned rgb, int opacity)
    {
        uint8_t a = (uint8_t)(std::clamp(opacity, 0, 100) * 255 / 100);
        return winrt::Windows::UI::Color{ a, (uint8_t)((rgb >> 16) & 0xFF), (uint8_t)((rgb >> 8) & 0xFF), (uint8_t)(rgb & 0xFF) };
    }

    // Кисти строятся один раз на набор настроек.
    void EnsureBrushes(const Config& c)
    {
        wchar_t sig[96];
        _snwprintf_s(sig, _TRUNCATE, L"%d/%u/%d/%d", c.bgMode, c.color, c.opacity, c.shape);
        if (g_brushSig == sig && g_barBrush && g_islandBrush) return;
        g_brushSig = sig;

        const winrt::Windows::UI::Color clear{ 0, 0, 0, 0 };
        auto solid = [&](winrt::Windows::UI::Color col) { return med::Brush(med::SolidColorBrush(col)); };
        auto acrylic = [&]()
        {
            med::AcrylicBrush b;
            b.BackgroundSource(med::AcrylicBackgroundSource::Backdrop);
            b.TintColor(Rgba(c.color, 100));
            b.TintOpacity(c.opacity / 100.0);
            b.FallbackColor(Rgba(c.color, 100));
            return med::Brush(b);
        };

        // фон самой панели
        if (c.bgMode == 1) g_barBrush = solid(Rgba(c.color, c.opacity));
        else if (c.bgMode == 2) g_barBrush = solid(clear);
        else if (c.bgMode == 3) g_barBrush = acrylic();
        else g_barBrush = nullptr; // 0 — оставить родную кисть Windows

        // фон островов: «как в Windows» для них — просто выбранный цвет
        if (c.bgMode == 3) g_islandBrush = acrylic();
        else if (c.bgMode == 2) g_islandBrush = solid(clear);
        else g_islandBrush = solid(Rgba(c.color, c.opacity));
    }

    bool NeedsIslands(const Config& c) { return c.enabled && (c.shape == 3 || ((c.shape == 1 || c.shape == 2) && c.keepEdges)); }

    // ---------- фон и форма панели ----------
    void ApplyRect(Item& it, const Config& c)
    {
        auto rect = it.obj.try_as<shp::Rectangle>();
        if (!rect) return;
        const bool stroke = it.kind == Kind::BgStroke;

        if (!it.saved)
        {
            it.origFill = rect.Fill();
            it.origMargin = rect.Margin();
            it.origRx = rect.RadiusX();
            it.origRy = rect.RadiusY();
            it.origOpacity = rect.Opacity();
            it.saved = true;
        }

        if (!c.enabled)
        {
            if (!stroke) rect.Fill(it.origFill);
            rect.Margin(it.origMargin);
            rect.RadiusX(it.origRx);
            rect.RadiusY(it.origRy);
            rect.Opacity(it.origOpacity);
            it.ourFill = nullptr;
            return;
        }

        xaml::Thickness m = it.origMargin;
        double rx = it.origRx, ry = it.origRy;
        if (c.shape == 1)
        {
            m = xaml::Thickness{ (double)c.side, it.origMargin.Top, (double)c.side, (double)c.gap };
            rx = ry = c.radius;
        }
        else if (c.shape == 2)
        {
            // нижние углы уходят за край экрана — остаётся «вкладка» со скруглённым верхом
            m = xaml::Thickness{ (double)c.side, it.origMargin.Top, (double)c.side, -(double)c.radius };
            rx = ry = c.radius;
        }
        rect.Margin(m);
        rect.RadiusX(rx);
        rect.RadiusY(ry);

        if (stroke)
        {
            rect.Opacity((c.hideStroke || c.shape == 3) ? 0.0 : it.origOpacity);
            return;
        }

        EnsureBrushes(c);
        if (c.shape == 3)
        {
            rect.Fill(med::SolidColorBrush(winrt::Windows::UI::Color{ 0, 0, 0, 0 })); // у островов самой панели нет
            it.ourFill = rect.Fill();
        }
        else if (c.bgMode == 0)
        {
            rect.Fill(it.origFill);
            it.ourFill = nullptr;
        }
        else
        {
            rect.Fill(g_barBrush);
            it.ourFill = g_barBrush;
        }
    }

    // ---------- острова ----------
    void RemoveIslands()
    {
        for (auto& h : g_hosts)
        {
            try
            {
                auto children = h.panel.Children();
                for (auto& b : h.borders)
                {
                    uint32_t idx = 0;
                    if (children.IndexOf(b, idx)) children.RemoveAt(idx);
                }
            }
            catch (...) { /* панель уже пересоздана — убирать нечего */ }
            h.borders.clear();
        }
        g_hosts.clear();
        g_islandSig.clear();
        g_nIslands = 0;
    }

    void RebuildHosts()
    {
        // хозяин островов — панель, в которой лежит фон; для каждой панели задач (каждого монитора) свой
        std::vector<Host> fresh;
        for (auto& kv : g_items)
        {
            if (kv.second.kind != Kind::BgFill) continue;
            auto rect = kv.second.obj.try_as<shp::Rectangle>();
            if (!rect) continue;
            auto parent = med::VisualTreeHelper::GetParent(rect).try_as<ctl::Panel>();
            if (!parent) continue;
            Host h;
            h.panel = parent;
            h.fill = rect;
            // уже созданные острова этой панели переносим
            for (auto& old : g_hosts)
                if (old.panel == parent) { h.borders = old.borders; break; }
            fresh.push_back(std::move(h));
        }
        g_hosts = std::move(fresh);
    }

    int FindHost(xaml::DependencyObject start)
    {
        auto cur = start;
        for (int i = 0; i < 60 && cur; ++i)
        {
            cur = med::VisualTreeHelper::GetParent(cur);
            if (!cur) break;
            for (size_t k = 0; k < g_hosts.size(); ++k)
                if (g_hosts[k].panel == cur) return (int)k;
        }
        return -1;
    }

    struct Box { double l, t, r, b; };

    void UpdateIslands(const Config& c, bool force)
    {
        if (!NeedsIslands(c))
        {
            if (!g_hosts.empty()) RemoveIslands();
            return;
        }
        EnsureBrushes(c);
        RebuildHosts();

        std::vector<std::vector<Box>> boxes(g_hosts.size());
        const bool edgesOnly = c.shape != 3;
        for (auto& kv : g_items)
        {
            auto& it = kv.second;
            const bool take = edgesOnly ? it.kind == Kind::Edge : (it.kind == Kind::Member || it.kind == Kind::Edge);
            if (!take) continue;
            auto fe = it.obj.try_as<xaml::FrameworkElement>();
            if (!fe) continue;
            try
            {
                if (fe.Visibility() == xaml::Visibility::Collapsed) continue;
                double w = fe.ActualWidth(), h = fe.ActualHeight();
                if (w < 2 || h < 2) continue;
                int hostIdx = FindHost(fe);
                if (hostIdx < 0) continue;
                auto pt = fe.TransformToVisual(g_hosts[hostIdx].panel).TransformPoint(wf::Point{ 0, 0 });
                boxes[hostIdx].push_back(Box{ pt.X, pt.Y, pt.X + w, pt.Y + h });
            }
            catch (...) { /* элемент как раз пересобирается — в следующий раз */ }
        }

        int total = 0;
        std::wstring sig;
        std::vector<std::vector<Box>> final(g_hosts.size());
        for (size_t k = 0; k < g_hosts.size(); ++k)
        {
            auto& list = boxes[k];
            std::sort(list.begin(), list.end(), [](const Box& a, const Box& b) { return a.l < b.l; });
            std::vector<Box> merged;
            const bool group = c.islandMode == 1 || edgesOnly;
            for (auto& b : list)
            {
                if (group && !merged.empty() && b.l - merged.back().r <= 16)
                {
                    merged.back().r = std::max(merged.back().r, b.r);
                    merged.back().t = std::min(merged.back().t, b.t);
                    merged.back().b = std::max(merged.back().b, b.b);
                }
                else merged.push_back(b);
            }
            double hostH = 0;
            try { hostH = g_hosts[k].panel.ActualHeight(); } catch (...) {}
            for (auto& b : merged)
            {
                Box o = b;
                if (group) { o.l -= c.islandPad; o.r += c.islandPad; }
                else { o.l += c.islandGap / 2.0; o.r -= c.islandGap / 2.0; }
                const double vin = 3;
                o.t += vin;
                o.b = (c.islandStyle == 1 && !edgesOnly) ? hostH + c.radius : b.b - vin;
                if (o.r - o.l < 4 || o.b - o.t < 4) continue;
                final[k].push_back(o);
                ++total;
                wchar_t s[96];
                _snwprintf_s(s, _TRUNCATE, L"%d:%.0f,%.0f,%.0f,%.0f;", (int)k, o.l, o.t, o.r, o.b);
                sig += s;
            }
        }
        wchar_t tail[64];
        _snwprintf_s(tail, _TRUNCATE, L"|%d/%d/%d/%d", c.radius, c.islandStyle, c.islandMode, edgesOnly ? 1 : 0);
        sig += tail;
        sig += g_brushSig;
        if (!force && sig == g_islandSig) return;
        g_islandSig = sig;
        g_nIslands = total;

        for (size_t k = 0; k < g_hosts.size(); ++k)
        {
            auto& host = g_hosts[k];
            try
            {
                auto children = host.panel.Children();
                auto& want = final[k];

                while (host.borders.size() > want.size())
                {
                    uint32_t idx = 0;
                    if (children.IndexOf(host.borders.back(), idx)) children.RemoveAt(idx);
                    host.borders.pop_back();
                }
                while (host.borders.size() < want.size())
                {
                    ctl::Border b;
                    b.IsHitTestVisible(false);
                    b.HorizontalAlignment(xaml::HorizontalAlignment::Left);
                    b.VerticalAlignment(xaml::VerticalAlignment::Top);
                    if (auto grid = host.panel.try_as<ctl::Grid>())
                    {
                        ctl::Grid::SetRow(b, 0);
                        ctl::Grid::SetColumn(b, 0);
                        ctl::Grid::SetRowSpan(b, std::max<int>(1, (int)grid.RowDefinitions().Size()));
                        ctl::Grid::SetColumnSpan(b, std::max<int>(1, (int)grid.ColumnDefinitions().Size()));
                    }
                    uint32_t pos = 0;
                    if (children.IndexOf(host.fill, pos)) ++pos; else pos = std::min<uint32_t>(1, children.Size());
                    pos = std::min<uint32_t>(pos + (uint32_t)host.borders.size(), children.Size());
                    children.InsertAt(pos, b); // сразу над фоном панели и под значками
                    host.borders.push_back(b);
                }

                for (size_t i = 0; i < want.size(); ++i)
                {
                    auto& b = host.borders[i];
                    const Box& o = want[i];
                    const double w = o.r - o.l, h = o.b - o.t;
                    double r = std::min<double>(c.radius, (c.islandStyle == 1 && !edgesOnly ? w : std::min(w, h)) * 0.4);
                    b.Margin(xaml::Thickness{ o.l, o.t, 0, 0 });
                    b.Width(w);
                    b.Height(h);
                    b.CornerRadius(xaml::CornerRadius{ r, r, r, r });
                    b.Background(g_islandBrush);
                }
            }
            catch (winrt::hresult_error const& e)
            {
                Log(L"острова: ошибка 0x%08X", (unsigned)e.code().value);
                host.borders.clear(); // дерево, вероятно, пересоздано
            }
        }
    }

    // ---------- применение ко всему ----------
    void ApplyAll()
    {
        try
        {
            auto c = CurrentConfig();
            for (auto& kv : g_items)
                if (kv.second.kind == Kind::BgFill || kv.second.kind == Kind::BgStroke)
                {
                    try { ApplyRect(kv.second, c); }
                    catch (winrt::hresult_error const& e) { Log(L"применение: ошибка 0x%08X", (unsigned)e.code().value); }
                }
            UpdateIslands(c, true);
            g_applied = c.enabled;
        }
        catch (...) { Log(L"ApplyAll: неожиданная ошибка"); }
    }

    // Раз в 250 мс: острова следуют за значками; раз в секунду проверяем, что Windows не вернула свой фон.
    void OnTick()
    {
        try
        {
            auto c = CurrentConfig();
            if (!c.enabled) return;
            UpdateIslands(c, false);
            if (++g_tick % 4 == 0)
                for (auto& kv : g_items)
                {
                    auto& it = kv.second;
                    if (it.kind != Kind::BgFill || !it.ourFill) continue;
                    auto rect = it.obj.try_as<shp::Rectangle>();
                    if (rect && rect.Fill() != it.ourFill) ApplyRect(it, c);
                }
        }
        catch (...) { /* следующий тик */ }
    }

    void EnsureQueue()
    {
        if (g_queue) return;
        try
        {
            g_queue = winrt::Windows::System::DispatcherQueue::GetForCurrentThread();
            if (!g_queue) { Log(L"у потока XAML нет очереди DispatcherQueue"); return; }
            g_queueOk = true;
            g_timer = g_queue.CreateTimer();
            g_timer.Interval(std::chrono::milliseconds(250));
            g_timer.Tick([](auto&&, auto&&) { OnTick(); });
            g_timer.Start();
        }
        catch (winrt::hresult_error const& e) { Log(L"очередь XAML: ошибка 0x%08X", (unsigned)e.code().value); }
    }
}

namespace Styler
{
    void SetDiagnostics(IXamlDiagnostics* diag)
    {
        if (diag) diag->AddRef();
        g_diag = diag;
    }

    HRESULT OnChange(const ParentChildRelation& relation, const VisualElement& element, VisualMutationType mutation)
    {
        try
        {
            const uint64_t key = Key(element.Handle);
            if (mutation == Remove) // перечисление VisualMutationType: Add, Remove
            {
                {
                    std::lock_guard<std::mutex> l(g_treeLock);
                    g_nodes.erase(key);
                }
                auto it = g_items.find(key);
                if (it != g_items.end())
                {
                    switch (it->second.kind)
                    {
                        case Kind::BgFill: --g_nBgFill; break;
                        case Kind::BgStroke: --g_nBgStroke; break;
                        case Kind::Member: --g_nMembers; break;
                        case Kind::Edge: --g_nEdges; break;
                    }
                    g_items.erase(it);
                }
                return S_OK;
            }

            const std::wstring type = element.Type ? element.Type : L"";
            const std::wstring name = element.Name ? element.Name : L"";
            {
                std::lock_guard<std::mutex> l(g_treeLock);
                if (g_nodes.size() < 30000)
                {
                    Node n;
                    n.parent = Key(relation.Parent);
                    n.index = relation.ChildIndex;
                    n.order = g_order++;
                    n.type = type;
                    n.name = name;
                    g_nodes[key] = std::move(n);
                }
            }

            EnsureQueue();

            Kind kind;
            if (type == L"Windows.UI.Xaml.Shapes.Rectangle" && name == L"BackgroundFill") kind = Kind::BgFill;
            else if (type == L"Windows.UI.Xaml.Shapes.Rectangle" && name == L"BackgroundStroke") kind = Kind::BgStroke;
            else if (IsMemberType(type)) kind = Kind::Member;
            else if (IsEdgeType(type)) kind = Kind::Edge;
            else return S_OK;

            if (!g_diag) return S_OK;
            IInspectable* raw = nullptr;
            if (FAILED(g_diag->GetIInspectableFromHandle(element.Handle, &raw)) || !raw) return S_OK;
            wf::IInspectable obj{ raw, winrt::take_ownership_from_abi };

            Item it;
            it.kind = kind;
            it.obj = obj;
            g_items[key] = std::move(it);
            switch (kind)
            {
                case Kind::BgFill: ++g_nBgFill; break;
                case Kind::BgStroke: ++g_nBgStroke; break;
                case Kind::Member: ++g_nMembers; break;
                case Kind::Edge: ++g_nEdges; break;
            }
            if (kind == Kind::BgFill || kind == Kind::BgStroke)
                ApplyRect(g_items[key], CurrentConfig()); // и при выключенном движке: запоминает исходный вид, чтобы его можно было вернуть
        }
        catch (winrt::hresult_error const& e) { Log(L"OnChange: ошибка 0x%08X", (unsigned)e.code().value); }
        catch (...) { Log(L"OnChange: неожиданная ошибка"); }
        return S_OK; // ошибка здесь не должна ломать панель
    }

    void SetConfig(const Config& cfg)
    {
        { std::lock_guard<std::mutex> l(g_cfgLock); g_cfg = cfg; }
        try
        {
            auto q = g_queue;
            if (q) q.TryEnqueue([] { ApplyAll(); });
        }
        catch (...) { Log(L"SetConfig: не удалось поставить задачу в очередь XAML"); }
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
        s.bgFill = g_nBgFill; s.bgStroke = g_nBgStroke; s.members = g_nMembers; s.edges = g_nEdges; s.islands = g_nIslands;
        s.applied = g_applied; s.queueOk = g_queueOk;
        return s;
    }
}
