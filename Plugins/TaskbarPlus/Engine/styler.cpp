// Сам «стилист»: находит на панели задач Windows 11 нужные элементы XAML и меняет их внешний вид.
// Всё, что трогает объекты XAML, выполняется только в потоке XAML (там, где панель их создала):
// из других потоков вызываются лишь SetConfig/WriteTree/GetStats, которые XAML-объектов не касаются.
//
// Что делает:
//  • фон и обводка панели (прозрачность, цвет, размытие, скругление, отступы) — элементы BackgroundFill / BackgroundStroke;
//  • три группы элементов — виджеты, системная область (часы, сеть, трей — ВСЕГДА одним объектом), значки приложений;
//  • расположение групп (слева / по центру / справа, порядок) — сдвигом RenderTransform, поэтому клики идут за значками;
//  • острова: скруглённые плитки под группами или под каждым значком, у каждого свой стиль.
#include "styler.h"

#undef GetCurrentTime
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.Foundation.Numerics.h>
#include <winrt/Windows.UI.h>
#include <winrt/Windows.UI.Xaml.h>
#include <winrt/Windows.UI.Xaml.Automation.h>
#include <winrt/Windows.UI.Xaml.Automation.Peers.h>
#include <winrt/Windows.UI.Xaml.Controls.h>
#include <winrt/Windows.UI.Xaml.Media.h>
#include <winrt/Windows.UI.Xaml.Shapes.h>

#include <algorithm>
#include <atomic>
#include <cmath>
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
namespace aut = winrt::Windows::UI::Xaml::Automation;

namespace
{
    enum class Kind { BgFill = 1, BgStroke = 2, App = 3, Widgets = 4, System = 5 };

    // номера групп в настройках: 0 виджеты, 1 системная область, 2 значки приложений
    int GroupOf(Kind k) { return k == Kind::Widgets ? 0 : k == Kind::System ? 1 : k == Kind::App ? 2 : -1; }

    struct Item
    {
        Kind kind = Kind::App;
        std::wstring type, name;
        wf::IInspectable obj{ nullptr };

        // исходный вид (для фона и обводки)
        bool saved = false;
        med::Brush origFill{ nullptr };
        xaml::Thickness origMargin{};
        double origRx = 0, origRy = 0, origOpacity = 1;
        med::Brush ourFill{ nullptr };

        // сдвиг (для групп)
        med::TranslateTransform tx{ nullptr };
        med::Transform prev{ nullptr };
        med::Transform installed{ nullptr };
        double dx = 0;
    };

    struct Host
    {
        ctl::Panel panel{ nullptr };
        xaml::UIElement fill{ nullptr };
        med::Brush nativeFill{ nullptr };   // родная кисть фона панели (для режима «как в Windows»)
        std::vector<ctl::Border> borders;
        bool seen = false;
    };

    struct Node
    {
        InstanceHandle handle = 0;
        uint64_t parent = 0;
        uint32_t index = 0;
        uint32_t order = 0;
        std::wstring type, name;
    };

    struct Box { double l = 0, t = 0, r = 0, b = 0; };

    struct Meas
    {
        Item* it = nullptr;
        uint64_t handle = 0;
        xaml::FrameworkElement fe{ nullptr };
        Box b;
        std::string key;       // для значков приложений
        std::wstring label;
    };

    struct Spec
    {
        int group = 0;         // 0 виджеты, 1 система, 2 значок
        std::string key;       // "widgets" / "system" / "app.<hex>"
        Box b;
    };

    // ----- состояние (поток XAML) -----
    IXamlDiagnostics* g_diag = nullptr;
    std::unordered_map<uint64_t, Item> g_items;
    std::vector<Host> g_hosts;
    xaml::DispatcherTimer g_timer{ nullptr };
    int g_tick = 0;
    int g_appliedGen = -1;
    std::wstring g_layoutSig;
    std::string g_islandsListed;
    std::vector<Rule> g_rules;
    std::string g_rulesSig = "?";

    // ----- общее для потоков -----
    std::mutex g_cfgLock;
    Config g_cfg;
    std::mutex g_treeLock;
    std::unordered_map<uint64_t, Node> g_nodes;
    uint32_t g_order = 0;
    std::atomic<int> g_nBgFill{ 0 }, g_nBgStroke{ 0 }, g_nApps{ 0 }, g_nWidgets{ 0 }, g_nSystem{ 0 }, g_nIslands{ 0 }, g_nHosts{ 0 }, g_nRects{ 0 };
    std::atomic<bool> g_applied{ false }, g_queueOk{ false };
    std::atomic<int> g_cfgGen{ 0 };
    std::mutex g_noteLock;
    std::wstring g_rectNames;     // имена прямоугольников, увиденных на панели (для отчёта, если фон не нашёлся)

    uint64_t Key(InstanceHandle h) { return (uint64_t)h; }
    Config CurrentConfig() { std::lock_guard<std::mutex> l(g_cfgLock); return g_cfg; }

    // ---------- правила классификации ----------
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

    std::vector<Rule> DefaultRules()
    {
        return {
            { 1, "Windows.UI.Xaml.Shapes.Rectangle", "BackgroundFill" },
            { 2, "Windows.UI.Xaml.Shapes.Rectangle", "BackgroundStroke" },
            { 3, "Taskbar.TaskListButton", "" },
            { 3, "Taskbar.ExperienceToggleButton", "" },
            { 3, "Taskbar.SearchBoxButton", "" },
            { 4, "Taskbar.AugmentedEntryPointButton", "" },
            { 5, "SystemTray.*", "" },
        };
    }

    int Classify(const std::wstring& type, const std::wstring& name)
    {
        for (auto& r : g_rules)
            if (Glob(r.type, type) && Glob(r.name, name)) return r.kind;
        return 0;
    }

    void SyncRules(const Config& c)
    {
        if (g_rulesSig == c.rulesSig && !g_rules.empty()) return;
        g_rulesSig = c.rulesSig;
        g_rules = c.rules.empty() ? DefaultRules() : c.rules;
    }

    // ---------- цвета и кисти ----------
    winrt::Windows::UI::Color Rgba(unsigned rgb, int opacity)
    {
        uint8_t a = (uint8_t)(std::clamp(opacity, 0, 100) * 255 / 100);
        return winrt::Windows::UI::Color{ a, (uint8_t)((rgb >> 16) & 0xFF), (uint8_t)((rgb >> 8) & 0xFF), (uint8_t)(rgb & 0xFF) };
    }

    med::Brush MakeBrush(int mode, unsigned color, int opacity)
    {
        if (mode == 1) return med::Brush(med::SolidColorBrush(Rgba(color, opacity)));
        if (mode == 3)
        {
            med::AcrylicBrush b;
            b.BackgroundSource(med::AcrylicBackgroundSource::Backdrop);
            b.TintColor(Rgba(color, 100));
            b.TintOpacity(opacity / 100.0);
            b.FallbackColor(Rgba(color, 100));
            return med::Brush(b);
        }
        return med::Brush(med::SolidColorBrush(winrt::Windows::UI::Color{ 0, 0, 0, 0 })); // 2 — прозрачный
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

    void GetCorner(const wf::IInspectable& o, double& rx, double& ry)
    {
        rx = ry = 0;
        if (auto r = o.try_as<shp::Rectangle>()) { rx = r.RadiusX(); ry = r.RadiusY(); }
        else if (auto b = o.try_as<ctl::Border>()) rx = ry = b.CornerRadius().TopLeft;
        else if (auto g = o.try_as<ctl::Grid>()) rx = ry = g.CornerRadius().TopLeft;
        else if (auto c = o.try_as<ctl::Control>()) rx = ry = c.CornerRadius().TopLeft;
    }

    void SetCorner(const wf::IInspectable& o, double rx, double ry)
    {
        if (auto r = o.try_as<shp::Rectangle>()) { r.RadiusX(rx); r.RadiusY(ry); }
        else if (auto b = o.try_as<ctl::Border>()) b.CornerRadius(xaml::CornerRadius{ rx, rx, rx, rx });
        else if (auto g = o.try_as<ctl::Grid>()) g.CornerRadius(xaml::CornerRadius{ rx, rx, rx, rx });
        else if (auto c = o.try_as<ctl::Control>()) c.CornerRadius(xaml::CornerRadius{ rx, rx, rx, rx });
    }

    bool NeedsIslands(const Config& c) { return c.enabled && (c.shape == 3 || ((c.shape == 1 || c.shape == 2) && c.keepEdges)); }

    // ---------- фон и форма панели ----------
    void ApplyBar(Item& it, const Config& c)
    {
        auto fe = it.obj.try_as<xaml::FrameworkElement>();
        if (!fe) return;
        const bool stroke = it.kind == Kind::BgStroke;

        if (!it.saved)
        {
            it.origFill = GetFill(it.obj);
            it.origMargin = fe.Margin();
            GetCorner(it.obj, it.origRx, it.origRy);
            it.origOpacity = fe.Opacity();
            it.saved = true;
        }

        if (!c.enabled)
        {
            if (!stroke) SetFill(it.obj, it.origFill);
            fe.Margin(it.origMargin);
            SetCorner(it.obj, it.origRx, it.origRy);
            fe.Opacity(it.origOpacity);
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
        fe.Margin(m);
        SetCorner(it.obj, rx, ry);

        if (stroke)
        {
            fe.Opacity((c.hideStroke || c.shape == 3) ? 0.0 : it.origOpacity);
            return;
        }

        if (c.shape == 3)
        {
            // у островов самой панели нет: фон прозрачный
            auto clear = MakeBrush(2, 0, 0);
            SetFill(it.obj, clear);
            it.ourFill = clear;
        }
        else if (c.bgMode == 0)
        {
            SetFill(it.obj, it.origFill);
            it.ourFill = nullptr;
        }
        else
        {
            auto br = MakeBrush(c.bgMode, c.color, c.opacity);
            SetFill(it.obj, br);
            it.ourFill = br;
        }
    }

    // ---------- сдвиг групп ----------
    void SetShift(Item& it, double dx)
    {
        auto fe = it.obj.try_as<xaml::FrameworkElement>();
        if (!fe) return;
        if (!it.tx)
        {
            if (std::abs(dx) < 0.5) return;
            it.prev = fe.RenderTransform();
            it.tx = med::TranslateTransform();
            if (it.prev)
            {
                med::TransformGroup g;
                g.Children().Append(it.prev);
                g.Children().Append(it.tx);
                it.installed = g;
            }
            else it.installed = it.tx;
            fe.RenderTransform(it.installed);
        }
        it.dx = dx;
        it.tx.X(dx);
    }

    void ClearShift(Item& it)
    {
        if (!it.tx && !it.installed) return;
        try
        {
            if (auto fe = it.obj.try_as<xaml::FrameworkElement>())
                if (fe.RenderTransform() == it.installed) fe.RenderTransform(it.prev);
        }
        catch (...) {}
        it.tx = nullptr;
        it.prev = nullptr;
        it.installed = nullptr;
        it.dx = 0;
    }

    void ResetShifts()
    {
        for (auto& kv : g_items)
            if (kv.second.tx) { try { kv.second.tx.X(0); } catch (...) {} }
    }

    void ClearAllShifts()
    {
        for (auto& kv : g_items) ClearShift(kv.second);
    }

    // Windows могла заменить RenderTransform (например, для своей анимации) — тогда ставим свой сдвиг заново.
    void ReassertShifts()
    {
        for (auto& kv : g_items)
        {
            auto& it = kv.second;
            if (!it.installed) continue;
            try
            {
                auto fe = it.obj.try_as<xaml::FrameworkElement>();
                if (fe && fe.RenderTransform() != it.installed)
                {
                    const double dx = it.dx;
                    it.tx = nullptr; it.prev = nullptr; it.installed = nullptr;
                    SetShift(it, dx);
                }
            }
            catch (...) {}
        }
    }

    // ---------- хозяин: панель XAML, в которой лежат фон и значки ----------
    xaml::DependencyObject ParentOf(const xaml::DependencyObject& o)
    {
        try { return med::VisualTreeHelper::GetParent(o); } catch (...) { return nullptr; }
    }

    // Родитель фона панели — «правильный» хозяин: острова ложатся прямо над фоном и под значками.
    // Если фон не нашёлся — берём самую широкую панель над значком (она занимает всё окно панели).
    int HostIndexFor(const xaml::FrameworkElement& fe)
    {
        xaml::DependencyObject cur = fe;
        ctl::Panel widest{ nullptr };
        double rootW = 0;
        try { if (auto xr = fe.XamlRoot()) rootW = xr.Size().Width; } catch (...) {}

        for (int i = 0; i < 80; ++i)
        {
            cur = ParentOf(cur);
            if (!cur) break;
            for (size_t k = 0; k < g_hosts.size(); ++k)
                if (g_hosts[k].fill && g_hosts[k].panel == cur) { g_hosts[k].seen = true; return (int)k; }
            if (auto p = cur.try_as<ctl::Panel>())
            {
                if (!widest) widest = p;
                try { if (rootW > 0 && p.ActualWidth() >= rootW - 2) { widest = p; break; } } catch (...) {}
            }
        }
        if (!widest) return -1;
        for (size_t k = 0; k < g_hosts.size(); ++k)
            if (g_hosts[k].panel == widest) { g_hosts[k].seen = true; return (int)k; }
        Host h;
        h.panel = widest;
        h.seen = true;
        g_hosts.push_back(std::move(h));
        return (int)g_hosts.size() - 1;
    }

    // Хозяева по найденным фонам панели (до замеров значков).
    void CollectBarHosts()
    {
        for (auto& h : g_hosts) h.seen = false;
        for (auto& kv : g_items)
        {
            if (kv.second.kind != Kind::BgFill) continue;
            auto fe = kv.second.obj.try_as<xaml::FrameworkElement>();
            if (!fe) continue;
            auto parent = ParentOf(fe).try_as<ctl::Panel>();
            if (!parent) continue;
            bool found = false;
            for (auto& h : g_hosts)
                if (h.panel == parent) { h.fill = fe; h.nativeFill = kv.second.saved ? kv.second.origFill : med::Brush{ nullptr }; h.seen = true; found = true; break; }
            if (found) continue;
            Host h;
            h.panel = parent;
            h.fill = fe;
            h.nativeFill = kv.second.saved ? kv.second.origFill : med::Brush{ nullptr };
            h.seen = true;
            g_hosts.push_back(std::move(h));
        }
    }

    void RemoveBorders(Host& h)
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

    void RemoveIslands()
    {
        for (auto& h : g_hosts) RemoveBorders(h);
        g_hosts.clear();
        g_layoutSig.clear();
        g_nIslands = 0;
        g_nHosts = 0;
    }

    // ---------- имена значков ----------
    std::string HexOf(const std::wstring& w)
    {
        std::string u = ToUtf8(w), out;
        static const char* d = "0123456789abcdef";
        for (unsigned char ch : u) { out += d[ch >> 4]; out += d[ch & 15]; }
        return out;
    }

    // Ключ значка: по AutomationId, а если его нет — по имени без хвоста « - 2 окна».
    void AppKeyName(const xaml::FrameworkElement& fe, std::string& key, std::wstring& label)
    {
        std::wstring id, name;
        try
        {
            auto peer = xaml::Automation::Peers::FrameworkElementAutomationPeer::CreatePeerForElement(fe);
            if (peer) { id = peer.GetAutomationId().c_str(); name = peer.GetName().c_str(); }
        }
        catch (...) {}
        if (id.empty()) { try { id = aut::AutomationProperties::GetAutomationId(fe).c_str(); } catch (...) {} }
        if (name.empty()) { try { name = aut::AutomationProperties::GetName(fe).c_str(); } catch (...) {} }
        auto cut = name.find(L" - ");
        if (cut != std::wstring::npos) name = name.substr(0, cut);
        label = name.empty() ? id : name;
        if (!id.empty()) key = "app." + HexOf(L"i:" + id);
        else if (!name.empty()) key = "app." + HexOf(L"n:" + name);
        else key.clear();
    }

    // ---------- стиль одного острова ----------
    IslandStyle EffectiveStyle(const Config& c, int group, const std::string& key)
    {
        IslandStyle r;
        auto merge = [&](const std::string& k)
        {
            auto it = c.styles.find(k);
            if (it == c.styles.end()) return;
            const auto& s = it->second;
            if (r.radius < 0) r.radius = s.radius;
            if (r.color < 0) r.color = s.color;
            if (r.opacity < 0) r.opacity = s.opacity;
            if (r.mode < 0) r.mode = s.mode;
            if (s.hidden) r.hidden = true;
        };
        merge(key);
        merge(group == 0 ? "widgets" : group == 1 ? "system" : "apps");
        return r;
    }

    // ---------- измерение, расположение, острова ----------
    std::wstring Signature(const Config& c)
    {
        std::wstring sig;
        wchar_t buf[96];
        _snwprintf_s(buf, _TRUNCATE, L"%d|", g_cfgGen.load());
        sig += buf;
        for (auto& kv : g_items)
        {
            auto& it = kv.second;
            if (it.kind == Kind::BgFill || it.kind == Kind::BgStroke) continue;
            try
            {
                auto fe = it.obj.try_as<xaml::FrameworkElement>();
                if (!fe) continue;
                auto off = fe.ActualOffset();
                _snwprintf_s(buf, _TRUNCATE, L"%llu:%d,%d,%d,%d,%d;", (unsigned long long)kv.first, (int)fe.Visibility(),
                    (int)std::lround(fe.ActualWidth() * 2), (int)std::lround(fe.ActualHeight() * 2),
                    (int)std::lround(off.x * 2), (int)std::lround(off.y * 2));
                sig += buf;
            }
            catch (...) {}
        }
        (void)c;
        return sig;
    }

    // Элемент лежит внутри другого элемента той же группы (его двигать не надо — он поедет вместе с родителем).
    bool HasAncestorInKind(uint64_t handle, Kind kind)
    {
        std::lock_guard<std::mutex> l(g_treeLock);
        auto n = g_nodes.find(handle);
        for (int i = 0; i < 200 && n != g_nodes.end(); ++i)
        {
            if (n->second.parent == 0) return false;
            auto p = g_items.find(n->second.parent);
            if (p != g_items.end() && p->second.kind == kind) return true;
            n = g_nodes.find(n->second.parent);
        }
        return false;
    }

    // Куда по умолчанию сидит группа: виджеты слева, система справа, значки — по центру (или слева, если панель выровнена влево).
    int DefaultZone(int group, const Box& b, double width)
    {
        if (group == 0) return 1;
        if (group == 1) return 3;
        const double mid = (b.l + b.r) / 2;
        return (b.l > width * 0.15 && mid > width * 0.35 && mid < width * 0.65) ? 2 : 1;
    }

    // Рассчитывает сдвиг каждой группы. Группы с «как в Windows» не двигаются; двигаются только те, что выбраны явно.
    void ComputeShifts(const Config& c, bool present[3], const Box gb[3], double width, double dx[3])
    {
        dx[0] = dx[1] = dx[2] = 0;
        if (width <= 0) return;
        const double gap = 8;
        int defZone[3];
        for (int g = 0; g < 3; ++g) defZone[g] = present[g] ? DefaultZone(g, gb[g], width) : 0;

        for (int z = 1; z <= 3; ++z)
        {
            // участники зоны по порядку слева направо
            std::vector<int> members;
            bool active = false;
            for (int k = 0; k < 3; ++k)
            {
                int g = c.order[k];
                if (!present[g]) continue;
                const int eff = c.zone[g] != 0 ? c.zone[g] : defZone[g];
                if (eff != z) continue;
                members.push_back(g);
                if (c.zone[g] != 0 && c.zone[g] != defZone[g]) active = true;
            }
            if (!active || members.empty()) continue;

            if (z == 2)
            {
                // по центру: все участники собираются в один блок посередине (значки «по умолчанию» тоже подвинутся)
                double total = 0;
                for (int g : members) total += gb[g].r - gb[g].l;
                total += gap * (members.size() - 1);
                double cursor = (width - total) / 2;
                for (int g : members) { dx[g] = cursor - gb[g].l; cursor += gb[g].r - gb[g].l + gap; }
            }
            else if (z == 1)
            {
                // слева: неподвижные («как в Windows») участники — опора, остальные встают вплотную после них
                double cursor = c.edgeMargin;
                for (int g : members) if (c.zone[g] == 0) cursor = std::max(cursor, gb[g].r + gap);
                for (int g : members)
                {
                    if (c.zone[g] == 0) continue;
                    dx[g] = cursor - gb[g].l;
                    cursor += gb[g].r - gb[g].l + gap;
                }
            }
            else
            {
                // справа: то же от правого края; порядок слева направо сохраняется
                double cursor = width - c.edgeMargin;
                for (int g : members) if (c.zone[g] == 0) cursor = std::min(cursor, gb[g].l - gap);
                for (auto it = members.rbegin(); it != members.rend(); ++it)
                {
                    int g = *it;
                    if (c.zone[g] == 0) continue;
                    const double w = gb[g].r - gb[g].l;
                    dx[g] = (cursor - w) - gb[g].l;
                    cursor -= w + gap;
                }
            }
        }
    }

    void WriteIslandList(const std::vector<std::pair<std::string, std::wstring>>& apps, bool haveWidgets, bool haveSystem)
    {
        std::string text;
        if (haveWidgets) text += "widgets||\n";
        if (haveSystem) text += "system||\n";
        for (auto& a : apps) text += "app|" + a.first + "|" + HexOf(a.second) + "\n";
        if (text == g_islandsListed) return;
        g_islandsListed = text;
        WriteTextAtomic(DataDir() + L"\\taskbar-plus-engine-islands.txt", text);
    }

    void Relayout(const Config& c, bool force)
    {
        const bool wantIslands = NeedsIslands(c);
        const bool wantLayout = c.enabled && c.LayoutActive();
        if (!wantIslands && !wantLayout)
        {
            if (!g_hosts.empty()) RemoveIslands();
            ClearAllShifts();
            g_layoutSig.clear();
            return;
        }

        auto sig = Signature(c);
        if (!force && sig == g_layoutSig) return;
        g_layoutSig = sig;

        CollectBarHosts();
        ResetShifts(); // замеры делаем без наших сдвигов — так «базовые» места не зависят от прошлого расчёта

        std::vector<std::vector<Meas>> ms(g_hosts.size());
        for (auto& kv : g_items)
        {
            auto& it = kv.second;
            if (it.kind != Kind::App && it.kind != Kind::Widgets && it.kind != Kind::System) continue;
            auto fe = it.obj.try_as<xaml::FrameworkElement>();
            if (!fe) continue;
            try
            {
                if (fe.Visibility() == xaml::Visibility::Collapsed) continue;
                const double w = fe.ActualWidth(), h = fe.ActualHeight();
                if (w < 2 || h < 2) continue;
                const int hi = HostIndexFor(fe);
                if (hi < 0) continue;
                if ((size_t)hi >= ms.size()) ms.resize(g_hosts.size());
                auto pt = fe.TransformToVisual(g_hosts[hi].panel).TransformPoint(wf::Point{ 0, 0 });
                Meas m;
                m.it = &it;
                m.handle = kv.first;
                m.fe = fe;
                m.b = Box{ pt.X, pt.Y, pt.X + w, pt.Y + h };
                if (it.kind == Kind::App) AppKeyName(fe, m.key, m.label);
                ms[hi].push_back(std::move(m));
            }
            catch (...) { /* элемент как раз пересобирается — в следующий раз */ }
        }
        if (ms.size() < g_hosts.size()) ms.resize(g_hosts.size());

        // убрать хозяев, которых больше нет
        for (size_t k = g_hosts.size(); k-- > 0;)
            if (!g_hosts[k].seen && ms[k].empty()) { RemoveBorders(g_hosts[k]); g_hosts.erase(g_hosts.begin() + k); ms.erase(ms.begin() + k); }
        g_nHosts = (int)g_hosts.size();

        int totalIslands = 0;
        std::vector<std::pair<std::string, std::wstring>> listApps;
        bool listWidgets = false, listSystem = false;
        std::set<Item*> measured;

        for (size_t hk = 0; hk < g_hosts.size(); ++hk)
        {
            auto& host = g_hosts[hk];
            auto& list = ms[hk];

            bool present[3] = { false, false, false };
            Box gb[3];
            for (auto& m : list)
            {
                const int g = GroupOf(m.it->kind);
                if (!present[g]) { gb[g] = m.b; present[g] = true; }
                else
                {
                    gb[g].l = std::min(gb[g].l, m.b.l); gb[g].r = std::max(gb[g].r, m.b.r);
                    gb[g].t = std::min(gb[g].t, m.b.t); gb[g].b = std::max(gb[g].b, m.b.b);
                }
            }

            double width = 0, hostH = 0;
            try { width = host.panel.ActualWidth(); hostH = host.panel.ActualHeight(); } catch (...) {}

            double dx[3] = { 0, 0, 0 };
            if (wantLayout) ComputeShifts(c, present, gb, width, dx);

            // сдвигаем только «верхние» элементы групп (вложенные поедут вместе с ними)
            for (auto& m : list)
            {
                const int g = GroupOf(m.it->kind);
                measured.insert(m.it);
                if (HasAncestorInKind(m.handle, m.it->kind)) { ClearShift(*m.it); continue; }
                try
                {
                    if (std::abs(dx[g]) >= 0.5) SetShift(*m.it, dx[g]);
                    else ClearShift(*m.it);
                }
                catch (...) {}
            }

            if (!wantIslands) continue;

            // ----- острова -----
            std::vector<Spec> specs;
            const bool edgesOnly = c.shape != 3;
            if (present[0]) { specs.push_back(Spec{ 0, "widgets", Box{ gb[0].l + dx[0], gb[0].t, gb[0].r + dx[0], gb[0].b } }); listWidgets = true; }
            if (present[1]) { specs.push_back(Spec{ 1, "system", Box{ gb[1].l + dx[1], gb[1].t, gb[1].r + dx[1], gb[1].b } }); listSystem = true; }
            if (!edgesOnly)
            {
                std::vector<Meas*> apps;
                for (auto& m : list) if (m.it->kind == Kind::App) apps.push_back(&m);
                std::sort(apps.begin(), apps.end(), [](const Meas* a, const Meas* b) { return a->b.l < b->b.l; });
                if (c.islandMode == 1)
                {
                    std::vector<Box> merged;
                    for (auto* m : apps)
                    {
                        if (!merged.empty() && m->b.l - merged.back().r <= 16)
                        {
                            merged.back().r = std::max(merged.back().r, m->b.r);
                            merged.back().t = std::min(merged.back().t, m->b.t);
                            merged.back().b = std::max(merged.back().b, m->b.b);
                        }
                        else merged.push_back(m->b);
                    }
                    for (auto& b : merged) specs.push_back(Spec{ 2, "apps", Box{ b.l + dx[2], b.t, b.r + dx[2], b.b } });
                }
                else
                {
                    std::map<std::string, int> seenKeys;
                    for (auto* m : apps)
                    {
                        std::string key = m->key.empty() ? "apps" : m->key;
                        int n = ++seenKeys[key];
                        if (n > 1) key += "#" + std::to_string(n);
                        specs.push_back(Spec{ 2, key, Box{ m->b.l + dx[2], m->b.t, m->b.r + dx[2], m->b.b } });
                        if (!m->key.empty()) listApps.push_back({ key, m->label });
                    }
                }
            }

            // ----- рисуем -----
            std::vector<std::pair<Spec, IslandStyle>> draw;
            for (auto& sp : specs)
            {
                auto st = EffectiveStyle(c, sp.group, sp.key);
                if (st.hidden) continue;
                draw.push_back({ sp, st });
            }
            totalIslands += (int)draw.size();

            try
            {
                auto children = host.panel.Children();
                while (host.borders.size() > draw.size())
                {
                    uint32_t idx = 0;
                    if (children.IndexOf(host.borders.back(), idx)) children.RemoveAt(idx);
                    host.borders.pop_back();
                }
                while (host.borders.size() < draw.size())
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
                    if (host.fill && children.IndexOf(host.fill, pos)) ++pos; else pos = std::min<uint32_t>(1, children.Size());
                    pos = std::min<uint32_t>(pos + (uint32_t)host.borders.size(), children.Size());
                    children.InsertAt(pos, b); // сразу над фоном панели и под значками
                    host.borders.push_back(b);
                }

                for (size_t i = 0; i < draw.size(); ++i)
                {
                    auto& b = host.borders[i];
                    const Spec& sp = draw[i].first;
                    const IslandStyle& st = draw[i].second;
                    Box o = sp.b;

                    const bool tabStyle = c.islandStyle == 1 && !edgesOnly;
                    if (sp.group == 2 && c.islandMode != 1) { o.l += c.islandGap / 2.0; o.r -= c.islandGap / 2.0; }
                    else { o.l -= c.islandPad; o.r += c.islandPad; }
                    const double vin = 3;
                    o.t += vin;
                    o.b = tabStyle ? hostH + (st.radius >= 0 ? st.radius : c.radius) : sp.b.b - vin;
                    const double w = o.r - o.l, h = o.b - o.t;
                    if (w < 4 || h < 4) { b.Width(0); b.Height(0); continue; }

                    const double want = st.radius >= 0 ? st.radius : c.radius;
                    const double cap = (st.radius >= 0 ? 0.5 : 0.4) * (tabStyle ? w : std::min(w, h));
                    const double r = std::min(want, cap);

                    b.Margin(xaml::Thickness{ o.l, o.t, 0, 0 });
                    b.Width(w);
                    b.Height(h);
                    b.CornerRadius(xaml::CornerRadius{ r, r, r, r });

                    const int mode = st.mode >= 0 ? st.mode : c.bgMode;
                    const unsigned color = st.color >= 0 ? (unsigned)st.color : c.color;
                    const int opacity = st.opacity >= 0 ? st.opacity : c.opacity;
                    if (mode == 0 && host.nativeFill) b.Background(host.nativeFill);
                    else b.Background(MakeBrush(mode == 0 ? 1 : mode, color, opacity));
                }
            }
            catch (winrt::hresult_error const& e)
            {
                Log(L"острова: ошибка 0x%08X", (unsigned)e.code().value);
                host.borders.clear(); // дерево, вероятно, пересоздано
            }
        }

        for (auto& kv : g_items)
            if (GroupOf(kv.second.kind) >= 0 && !measured.count(&kv.second)) ClearShift(kv.second);

        g_nIslands = totalIslands;
        if (wantIslands) WriteIslandList(listApps, listWidgets, listSystem);
    }

    // ---------- применение ко всему ----------
    void RecountItems()
    {
        int bg = 0, st = 0, ap = 0, wi = 0, sy = 0;
        for (auto& kv : g_items)
            switch (kv.second.kind)
            {
                case Kind::BgFill: ++bg; break;
                case Kind::BgStroke: ++st; break;
                case Kind::App: ++ap; break;
                case Kind::Widgets: ++wi; break;
                case Kind::System: ++sy; break;
            }
        g_nBgFill = bg; g_nBgStroke = st; g_nApps = ap; g_nWidgets = wi; g_nSystem = sy;
    }

    bool AddItem(InstanceHandle handle, const std::wstring& type, const std::wstring& name, int kind)
    {
        if (!g_diag || kind == 0) return false;
        IInspectable* raw = nullptr;
        if (FAILED(g_diag->GetIInspectableFromHandle(handle, &raw)) || !raw) return false;
        wf::IInspectable obj{ raw, winrt::take_ownership_from_abi };
        Item it;
        it.kind = (Kind)kind;
        it.type = type;
        it.name = name;
        it.obj = obj;
        g_items[Key(handle)] = std::move(it);
        return true;
    }

    // Правила поменялись — разобрать уже увиденные элементы заново.
    void RescanAll(const Config& c)
    {
        SyncRules(c);
        std::vector<Node> snap;
        {
            std::lock_guard<std::mutex> l(g_treeLock);
            snap.reserve(g_nodes.size());
            for (auto& kv : g_nodes) snap.push_back(kv.second);
        }
        std::set<uint64_t> want;
        for (auto& n : snap)
        {
            const int kind = Classify(n.type, n.name);
            if (kind == 0) continue;
            const uint64_t k = Key(n.handle);
            want.insert(k);
            auto it = g_items.find(k);
            if (it != g_items.end() && (int)it->second.kind == kind) continue;
            if (it != g_items.end()) { ClearShift(it->second); g_items.erase(it); }
            try { AddItem(n.handle, n.type, n.name, kind); } catch (...) {}
        }
        for (auto it = g_items.begin(); it != g_items.end();)
        {
            if (!want.count(it->first)) { ClearShift(it->second); it = g_items.erase(it); }
            else ++it;
        }
        RecountItems();
    }

    void ApplyAll()
    {
        try
        {
            auto c = CurrentConfig();
            if (g_rulesSig != c.rulesSig || g_rules.empty()) RescanAll(c);
            for (auto& kv : g_items)
                if (kv.second.kind == Kind::BgFill || kv.second.kind == Kind::BgStroke)
                {
                    try { ApplyBar(kv.second, c); }
                    catch (winrt::hresult_error const& e) { Log(L"применение: ошибка 0x%08X", (unsigned)e.code().value); }
                }
            try { Relayout(c, true); }
            catch (winrt::hresult_error const& e) { Log(L"расположение: ошибка 0x%08X", (unsigned)e.code().value); }
            g_applied = c.enabled;
        }
        catch (...) { Log(L"ApplyAll: неожиданная ошибка"); }
    }

    // Раз в 250 мс: острова и расположение следуют за значками; раз в секунду проверяем, что Windows не вернула своё.
    void OnTick()
    {
        try
        {
            if (g_cfgGen.load() != g_appliedGen)
            {
                g_appliedGen = g_cfgGen.load();
                ApplyAll();
                return;
            }
            auto c = CurrentConfig();
            if (!c.enabled) return;
            try { Relayout(c, false); }
            catch (winrt::hresult_error const& e) { Log(L"расположение: ошибка 0x%08X", (unsigned)e.code().value); }
            if (++g_tick % 4 == 0)
            {
                for (auto& kv : g_items)
                {
                    auto& it = kv.second;
                    if (it.kind != Kind::BgFill || !it.ourFill) continue;
                    if (GetFill(it.obj) != it.ourFill) ApplyBar(it, c);
                }
                ReassertShifts();
            }
        }
        catch (...) { /* следующий тик */ }
    }

    void EnsureTimer()
    {
        if (g_timer) return;
        try
        {
            g_timer = xaml::DispatcherTimer();
            g_timer.Interval(std::chrono::milliseconds(250));
            g_timer.Tick([](auto&&, auto&&) { OnTick(); });
            g_timer.Start();
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
    void SetDiagnostics(IXamlDiagnostics* diag)
    {
        if (diag) diag->AddRef();
        g_diag = diag;
    }

    HRESULT OnChange(const ParentChildRelation& relation, const VisualElement& elementIn, VisualMutationType mutation)
    {
        // Строки в VisualElement отдаются нам во владение — освобождаем их сами.
        VisualElement element = elementIn;
        BstrGuard gFile(element.SrcInfo.FileName), gHash(element.SrcInfo.Hash), gName(element.Name), gType(element.Type);
        try
        {
            const uint64_t key = Key(element.Handle);
            if (mutation == Remove)
            {
                {
                    std::lock_guard<std::mutex> l(g_treeLock);
                    g_nodes.erase(key);
                }
                auto it = g_items.find(key);
                if (it != g_items.end())
                {
                    ClearShift(it->second);
                    g_items.erase(it);
                    RecountItems();
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
            if (kind != 0 && AddItem(element.Handle, type, name, kind))
            {
                RecountItems();
                auto& it = g_items[key];
                if (it.kind == Kind::BgFill || it.kind == Kind::BgStroke)
                    ApplyBar(it, c); // и при выключенном движке: запоминает исходный вид, чтобы его можно было вернуть
            }
        }
        catch (winrt::hresult_error const& e) { Log(L"OnChange: ошибка 0x%08X", (unsigned)e.code().value); }
        catch (...) { Log(L"OnChange: неожиданная ошибка"); }
        try
        {
            if (g_cfgGen.load() != g_appliedGen) { g_appliedGen = g_cfgGen.load(); ApplyAll(); } // запасной путь, если таймер XAML не тикает
        }
        catch (...) {}
        return S_OK; // ошибка здесь не должна ломать панель
    }

    void SetConfig(const Config& cfg)
    {
        { std::lock_guard<std::mutex> l(g_cfgLock); g_cfg = cfg; }
        ++g_cfgGen; // применит поток XAML на ближайшем тике (раз в 250 мс)
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
        s.bgFill = g_nBgFill; s.bgStroke = g_nBgStroke; s.apps = g_nApps; s.widgets = g_nWidgets; s.system = g_nSystem;
        s.islands = g_nIslands; s.hosts = g_nHosts; s.rects = g_nRects;
        s.applied = g_applied; s.queueOk = g_queueOk;
        { std::lock_guard<std::mutex> l(g_noteLock); s.rectNames = g_rectNames; }
        return s;
    }
}
