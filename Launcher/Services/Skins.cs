using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace BattleLauncher;

// Скин = цвета + скругления + шрифт + тени. Ничего исполняемого: только значения из этой таблицы. Лаунчер подменяет ресурсы окна
// на лету (DynamicResource), поэтому выбор действует сразу и запоминается в %APPDATA%\BattleLauncher\skin.txt.
// Расположение меню и блоков у всех скинов прежнее: меняется оформление, а не устройство окна (концепты на картинках — как задумано).
sealed record Skin(string Id, string Name, string Group, string Desc,
    string Bg, string Bg2, string Pattern,
    string Panel, string Panel2, string Line, string Text, string Muted, string Accent, string Accent2, string OnAccent,
    string Red, string Green, string Yellow, string Purple,
    int CardR, int BtnR, bool Brackets, string Shadow, string Font, string Mono);

static class Skins
{
    public const string DefaultId = "ghost";
    const string Seg = "Segoe UI", Bah = "Bahnschrift, Segoe UI", Geo = "Georgia, Segoe UI", Cas = "Cascadia Mono, Consolas, Courier New", Con = "Consolas, Courier New";

    // Pattern: scan (строки развёртки) | grid | hatch | solid | grad (диагональный градиент Bg → Bg2) | vgrad (вертикальный)
    // Shadow: none | neuL (светлый неоморфизм) | neuD (тёмный неоморфизм)
    static Skin K(string id, string name, string group, string desc, string bg, string bg2, string pat, string panel, string panel2, string line, string text, string muted,
        string acc, string acc2, string onAcc, string red, string green, string yellow, string purple, int cr, int br, bool brk, string shadow, string font, string mono)
        => new(id, name, group, desc, bg, bg2, pat, panel, panel2, line, text, muted, acc, acc2, onAcc, red, green, yellow, purple, cr, br, brk, shadow, font, mono);

    public static readonly Skin[] All =
    {
        K("ghost", "Ghost Shell (как сейчас)", "Киберпанк", "Фирменный неон: голубой и розовый на тёмно-фиолетовом, строки развёртки.", "#070314", "#110A2C", "scan",
            "#0D0821", "#170E36", "#4A22A0", "#EAF8FF", "#A08FE0", "#00F0FF", "#FF2E97", "#001418", "#FF3B5C", "#3CFF9A", "#FCEE0A", "#A259FF", 4, 0, true, "none", Seg, Cas),
        K("cp2", "Неоморфизм × Cyberpunk 2077", "Киберпанк", "По скриншотам игры: тёмно-красный фон, тонкие красные линии, голубое выделение, объёмные плиты.", "#14080B", "#070304", "vgrad",
            "#1A0D11", "#12070A", "#5A1D22", "#D4D4D4", "#8E7B7E", "#4DE8E6", "#FF5A5A", "#00201F", "#FF5A5A", "#4CF08A", "#FFE45E", "#B08CFF", 0, 0, false, "neuD", Bah, Con),
        K("neotokyo", "Нео-Токио", "Киберпанк", "Скошенный неон, линии развёртки, как HUD.", "#0A0714", "#4A22A022", "grid",
            "#140B2B", "#1D1042", "#4A22A0", "#EAF8FF", "#9A8EE0", "#00F0FF", "#FF2A9D", "#04010F", "#FF3B5C", "#3CFF9A", "#FCEE0A", "#A259FF", 0, 0, true, "none", Bah, Con),
        K("matrix", "Терминал «Матрица»", "Киберпанк", "Зелёное по чёрному, всё как консоль.", "#000000", "#000000", "solid",
            "#021006", "#04200C", "#0F5A2B", "#3DFF85", "#1F9C52", "#7DFFB0", "#3DFF85", "#000000", "#FF5555", "#7DFFB0", "#D8FF3D", "#3DFFD0", 0, 0, false, "none", Con, Con),
        K("glitch", "Глитч-Корпорация", "Киберпанк", "Жёлтое с чёрным, жёсткие углы, штриховка.", "#0E0E0E", "#FCEE0A10", "hatch",
            "#1A1A1A", "#262626", "#3A3A3A", "#F2F2F2", "#9A9A9A", "#FCEE0A", "#FF003C", "#000000", "#FF003C", "#3CFF9A", "#FCEE0A", "#B266FF", 0, 0, false, "none", Bah, Con),
        K("holo", "Голограмма", "Киберпанк", "Стеклянные голубые панели над сеткой.", "#020B1A", "#00F0FF22", "grid",
            "#06182B", "#0A2640", "#1D6F84", "#D8FBFF", "#6FB7C4", "#00F0FF", "#7AFFD9", "#00131A", "#FF6B9A", "#7AFFD9", "#FCEE0A", "#A259FF", 14, 14, false, "none", Seg, Con),
        K("circuit", "Нейро-схема", "Киберпанк", "Плата с «дорожками», бирюзовый и фиолетовый.", "#04120F", "#00FFC822", "grid",
            "#072A22", "#0A3A2F", "#0F6B55", "#D5FFF4", "#6FB9A6", "#00FFC8", "#B266FF", "#00201A", "#FF6B6B", "#00FFC8", "#FCEE0A", "#B266FF", 10, 6, false, "none", Seg, Con),
        K("mission", "Центр управления", "Киберпанк", "Тёмно-синий и янтарные показания.", "#0B1320", "#FFFFFF08", "scan",
            "#101C30", "#16263F", "#223654", "#D7E3F4", "#7F93AD", "#FFB000", "#4AA3FF", "#000000", "#FF5D5D", "#3CFF9A", "#FFB000", "#8E7BFF", 2, 2, false, "none", Bah, Con),
        // ----- Неоморфизм -----
        K("neumo", "Неоморфизм", "Неоморфизм", "Светло-серый, мягкие выдавленные формы.", "#E0E5EC", "#E0E5EC", "solid",
            "#E0E5EC", "#E0E5EC", "#F6F9FD", "#3D4852", "#7B8794", "#6C63FF", "#6C63FF", "#FFFFFF", "#D6455D", "#2E9E6A", "#B8901A", "#6C63FF", 18, 12, false, "neuL", Seg, Cas),
        K("neoa", "Нео-киберпанк", "Неоморфизм", "Тёмный неоморфизм с неоновым акцентом.", "#171326", "#171326", "solid",
            "#171326", "#1E1832", "#2B2447", "#EAF8FF", "#8F86C8", "#00F0FF", "#FF2A9D", "#04010F", "#FF3B5C", "#3CFF9A", "#FCEE0A", "#A259FF", 16, 10, false, "neuD", Bah, Con),
        K("neob", "Нео Hi-Fi", "Неоморфизм", "Светлый неоморфизм, оранжевый акцент, как аппаратура.", "#DDE0E5", "#DDE0E5", "solid",
            "#DDE0E5", "#DDE0E5", "#F7F8FA", "#33383F", "#767D88", "#FF8A1F", "#FF8A1F", "#FFFFFF", "#D6455D", "#27A35A", "#B8901A", "#7A5CFF", 14, 10, false, "neuL", Bah, Con),
        K("neoc", "Нео Material", "Неоморфизм", "Лавандовый, крупные скругления.", "#E8E3F4", "#E8E3F4", "solid",
            "#E8E3F4", "#E8E3F4", "#FBF9FF", "#3A3350", "#80779C", "#7A5CFF", "#B9A7FF", "#FFFFFF", "#D6455D", "#2E9E6A", "#B8901A", "#7A5CFF", 26, 20, false, "neuL", Seg, Cas),
        K("neod", "Нео-стекло", "Неоморфизм", "Матовое стекло на мягком градиенте.", "#DFE8FF", "#F6E1F1", "grad",
            "#99FFFFFF", "#B3FFFFFF", "#CCFFFFFF", "#2F3A55", "#6F7D9B", "#4F7CFF", "#FF8FB7", "#FFFFFF", "#D6455D", "#2E9E6A", "#B8901A", "#7A5CFF", 22, 16, false, "neuL", Seg, Cas),
        K("neoe", "Нео-лес", "Неоморфизм", "Шалфейный, с засечками.", "#DFE5D2", "#DFE5D2", "solid",
            "#DFE5D2", "#DFE5D2", "#F4F8EC", "#2F3B2A", "#738063", "#4F7A4A", "#D9B36A", "#FFFFFF", "#B5532E", "#3F7A3A", "#B8901A", "#7A5CA6", 22, 14, false, "neuL", Geo, Cas),
        K("neof", "Нео — центр управления", "Неоморфизм", "Тёмно-синий неоморфизм с янтарём.", "#16223A", "#16223A", "solid",
            "#16223A", "#1B2A47", "#26385A", "#D7E3F4", "#7F93AD", "#FFB000", "#4AA3FF", "#1A1000", "#FF6B6B", "#3CFF9A", "#FFB000", "#8E7BFF", 12, 8, false, "neuD", Bah, Con),
        // ----- Другие -----
        K("swiss", "Швейцарский минимализм", "Другие", "Белый, тонкие линии, красный акцент.", "#FFFFFF", "#FFFFFF", "solid",
            "#F4F4F2", "#FFFFFF", "#C8C8C8", "#111111", "#777777", "#E30613", "#111111", "#FFFFFF", "#E30613", "#0A8F3C", "#C99700", "#6D3BD6", 0, 0, false, "none", "Arial", Con),
        K("material", "Material You (тёмный)", "Другие", "Мягкие тона, большие скругления.", "#141218", "#141218", "solid",
            "#211F26", "#2B2930", "#3A3744", "#E6E0E9", "#CAC4D0", "#D0BCFF", "#EFB8C8", "#381E72", "#FFB4AB", "#A8E6A1", "#F2E07A", "#D0BCFF", 20, 20, false, "none", Seg, Cas),
        K("glass", "Стекло", "Другие", "Размытые панели на градиенте.", "#6A4CFF", "#FF7AB6", "grad",
            "#33FFFFFF", "#44FFFFFF", "#66FFFFFF", "#FFFFFF", "#EDE6FF", "#FFFFFF", "#FFD2E8", "#5B32C9", "#FFD0D8", "#C8FFD8", "#FFF2A8", "#E2D4FF", 20, 16, false, "none", Seg, Cas),
        K("paper", "Бумага и блокнот", "Другие", "Тёплая бумага, засечки.", "#F6EFE0", "#F6EFE0", "solid",
            "#FFFAF0", "#EFE4CB", "#D8C8A8", "#3B2F22", "#8A7A65", "#B5442E", "#E9C46A", "#FFFFFF", "#B5442E", "#4A7C3A", "#B8901A", "#7A4CC4", 3, 3, false, "none", Geo, Cas),
        K("brutal", "Брутализм", "Другие", "Яркие плоские цвета и чёрные линии.", "#FFFBE6", "#FFFBE6", "solid",
            "#FFFFFF", "#FFE34D", "#000000", "#000000", "#444444", "#FF5EA8", "#3DD6D0", "#000000", "#E30613", "#0A8F3C", "#FFE34D", "#A58BFF", 0, 0, false, "none", Bah, Con),
        K("win95", "Ретро Windows 95", "Другие", "Серый, классика.", "#C0C0C0", "#C0C0C0", "solid",
            "#C0C0C0", "#D4D0C8", "#808080", "#000000", "#404040", "#000080", "#008080", "#FFFFFF", "#A00000", "#006400", "#808000", "#800080", 0, 0, false, "none", "Tahoma, Segoe UI", "Courier New"),
        K("hifi", "Hi-Fi стойка", "Другие", "Тёмный металл и оранжевые индикаторы.", "#868B92", "#4A4E54", "vgrad",
            "#1A1C1F", "#2A2D31", "#000000", "#E8E8E8", "#9AA0A6", "#FF9F1A", "#FFBF5A", "#000000", "#FF5D5D", "#5DFF8A", "#FFD23A", "#B08CFF", 4, 3, false, "none", Bah, Con),
        K("kawaii", "Конфетный", "Другие", "Пастельный розовый, круглые формы.", "#FFF0F7", "#FFF0F7", "solid",
            "#FFFFFF", "#FFE0EF", "#FFC2DD", "#5B3A5F", "#A98AAA", "#FF7EB6", "#8FE3CF", "#FFFFFF", "#E0559C", "#38B08F", "#D9A020", "#B58CFF", 22, 20, false, "none", Seg, Cas),
        K("sunset", "Закат", "Другие", "Тёмный фон и тёплые градиентные акценты.", "#1A0F2E", "#2A1030", "grad",
            "#2A1A45", "#3A2560", "#5A3A8A", "#FFFFFF", "#C7B3E6", "#FF7A59", "#FFC857", "#FFFFFF", "#FF8A9A", "#7DFFB0", "#FFC857", "#8B5CFF", 22, 20, false, "none", Seg, Cas),
        K("eink", "Электронные чернила", "Другие", "Чёрное на белом, без цвета.", "#FFFFFF", "#FFFFFF", "solid",
            "#FFFFFF", "#F0F0F0", "#000000", "#000000", "#555555", "#000000", "#333333", "#FFFFFF", "#B00000", "#006000", "#555555", "#333333", 0, 0, false, "none", Geo, Con),
        K("bento2", "Цветные плитки", "Другие", "Светлый, крупные скругления.", "#F2F2F7", "#F2F2F7", "solid",
            "#FFFFFF", "#E8E8EF", "#D9D9E2", "#1C1C1E", "#6E6E73", "#007AFF", "#FF9F0A", "#FFFFFF", "#FF3B30", "#28A745", "#E08A00", "#AF52DE", 24, 16, false, "none", Seg, Cas),
        K("ide", "Редактор кода", "Другие", "Тёмный, как VS Code.", "#1E1E1E", "#1E1E1E", "solid",
            "#252526", "#37373D", "#3C3C3C", "#D4D4D4", "#858585", "#569CD6", "#4EC9B0", "#FFFFFF", "#F48771", "#4EC9B0", "#DCDCAA", "#C586C0", 3, 3, false, "none", Seg, Con),
        K("forest", "Лесной", "Другие", "Шалфей и песок, засечки.", "#E8E4D4", "#E8E4D4", "solid",
            "#F3EFE0", "#D9E2C8", "#D6D0B6", "#2F3B2A", "#7A8670", "#4F7A4A", "#D9B36A", "#FFFFFF", "#B5532E", "#3F7A3A", "#B8901A", "#7A5CA6", 18, 12, false, "none", Geo, Cas),
    };

    public static Skin ById(string? id) => All.FirstOrDefault(s => s.Id == id) ?? All[0];
    static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BattleLauncher", "skin.txt");
    public static Skin Current { get; private set; } = All[0];

    public static void LoadSaved()
    {
        try { if (File.Exists(FilePath)) Apply(ById(File.ReadAllText(FilePath).Trim()), false); } catch { /* битый файл — остаёмся на прежнем */ }
    }

    static SolidColorBrush Br(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    static Brush MakeBg(Skin s)
    {
        switch (s.Pattern)
        {
            case "grad":
            {
                var g = new LinearGradientBrush(C(s.Bg), C(s.Bg2), 45);
                g.Freeze();
                return g;
            }
            case "vgrad":
            {
                var g = new LinearGradientBrush(C(s.Bg), C(s.Bg2), 90);
                g.Freeze();
                return g;
            }
            case "solid":
                return Br(s.Bg);
            default:
            {
                // scan / grid / hatch: плитка 6×6 (или 24×24 у сетки) с тонким узором поверх цвета фона
                var line = s.Bg2;
                var group = new DrawingGroup();
                double sz = s.Pattern == "grid" ? 28 : 6;
                group.Children.Add(new GeometryDrawing(Br(s.Bg), null, new RectangleGeometry(new Rect(0, 0, sz, sz))));
                var pen = Br(line);
                if (s.Pattern == "scan") group.Children.Add(new GeometryDrawing(pen, null, new RectangleGeometry(new Rect(0, sz - 1, sz, 1))));
                else if (s.Pattern == "grid")
                {
                    group.Children.Add(new GeometryDrawing(pen, null, new RectangleGeometry(new Rect(0, sz - 1, sz, 1))));
                    group.Children.Add(new GeometryDrawing(pen, null, new RectangleGeometry(new Rect(sz - 1, 0, 1, sz))));
                }
                else // hatch
                    group.Children.Add(new GeometryDrawing(null, new Pen(pen, 1.5), new LineGeometry(new Point(0, sz), new Point(sz, 0))));
                var db = new DrawingBrush(group) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, sz, sz), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, sz, sz), ViewboxUnits = BrushMappingMode.Absolute };
                db.Freeze();
                return db;
            }
        }
    }

    static Effect? EffectFor(string kind) => kind switch
    {
        "neuL" => new DropShadowEffect { Color = Color.FromRgb(0xA3, 0xB1, 0xC6), BlurRadius = 16, ShadowDepth = 6, Direction = 315, Opacity = 0.75 },
        "neuD" => new DropShadowEffect { Color = Colors.Black, BlurRadius = 16, ShadowDepth = 6, Direction = 315, Opacity = 0.7 },
        _ => null,
    };

    static double Lum(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

    public static void Apply(Skin s, bool save = true)
    {
        var app = Application.Current;
        if (app == null) return;
        var r = app.Resources;
        r["BgSolid"] = Br(s.Bg);
        r["Bg"] = MakeBg(s);
        r["Panel"] = Br(s.Panel);
        r["Panel2"] = Br(s.Panel2);
        r["Line"] = Br(s.Line);
        r["Text"] = Br(s.Text);
        r["Muted"] = Br(s.Muted);
        r["Accent"] = Br(s.Accent);
        var ac = C(s.Accent);
        r["AccentSoft"] = Br($"#33{ac.R:X2}{ac.G:X2}{ac.B:X2}");
        r["OnAccent"] = Br(s.OnAccent);
        r["Accent2"] = Br(s.Accent2);
        r["Red"] = Br(s.Red);
        r["Green"] = Br(s.Green);
        r["Yellow"] = Br(s.Yellow);
        r["OnYellow"] = Br(Lum(C(s.Yellow)) > 0.55 ? "#171400" : "#FFFFFF");
        r["Purple"] = Br(s.Purple);
        r["CardRadius"] = new CornerRadius(s.CardR);
        r["BtnRadius"] = new CornerRadius(s.BtnR);
        r["BracketVis"] = s.Brackets ? Visibility.Visible : Visibility.Collapsed;
        var eff = EffectFor(s.Shadow);
        if (eff != null) { eff.Freeze(); r["CardEffect"] = eff; } else r["CardEffect"] = new DropShadowEffect { Opacity = 0, ShadowDepth = 0, BlurRadius = 0 };
        r["Mono"] = new FontFamily(s.Mono);
        foreach (Window w in app.Windows) w.FontFamily = new FontFamily(s.Font);
        Current = s;
        if (save)
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!); File.WriteAllText(FilePath, s.Id); } catch { }
        }
    }

    // Размер/шрифт для окон, которые открываются после выбора скина
    public static FontFamily MainFont => new(Current.Font);
}

static class SkinExt
{
    // Карточки, которые лаунчер строит кодом: берут скругление и тень из текущего скина и перерисовываются вместе с ним
    public static System.Windows.Controls.Border WithSkin(this System.Windows.Controls.Border b)
    {
        b.SetResourceReference(System.Windows.Controls.Border.CornerRadiusProperty, "CardRadius");
        b.SetResourceReference(UIElement.EffectProperty, "CardEffect");
        return b;
    }
}
