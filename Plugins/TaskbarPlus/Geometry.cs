using System.Globalization;
using System.IO;
using System.Text;

namespace TaskbarPlusApp;

// Прямоугольник в пикселях окна панели задач (от его левого верхнего угла).
readonly record struct Box(int L, int T, int R, int B)
{
    public int Width => R - L;
    public int Height => B - T;
}

// Где на одной панели лежат группы: значки приложений, виджеты, часы с треем, каждый значок отдельно.
// Движок (DLL в Проводнике) измеряет это в XAML и записывает в taskbar-plus-geometry.txt; здесь файл только читается.
sealed class BarGeometry
{
    public int RootW, RootH, Scale100 = 100;
    public Box? Widgets, System, Apps;
    public List<Box> Icons { get; } = new();
    public double Scale => Scale100 / 100.0;
}

sealed class GeometryFile
{
    public long Pid, Tick;
    public List<BarGeometry> Bars { get; } = new();

    public static readonly string DefaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BattleLauncher", "taskbar-plus-geometry.txt");

    // Формат (по строке на запись, поля через «|», числа — целые пиксели):
    //   v=1 / pid=… / tick=… / bars=…
    //   bar|<n>|<ширина окна>|<высота окна>|<масштаб×100>
    //   w|<n>|л|в|п|н  виджеты     s|<n>|…  системная область     a|<n>|…  все значки приложений     i|<n>|…  один значок
    public static GeometryFile? Parse(string text)
    {
        try
        {
            var f = new GeometryFile();
            var bars = new Dictionary<int, BarGeometry>();
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                var eq = line.IndexOf('=');
                if (eq > 0 && !line.Contains('|'))
                {
                    var k = line[..eq]; var v = line[(eq + 1)..];
                    if (k == "pid") long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out f.Pid);
                    else if (k == "tick") long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out f.Tick);
                    continue;
                }
                var p = line.Split('|');
                if (p.Length < 5 || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < 0 || n > 16) continue;
                int I(int idx) => int.Parse(p[idx], NumberStyles.Integer, CultureInfo.InvariantCulture);
                if (p[0] == "bar")
                {
                    bars[n] = new BarGeometry { RootW = I(2), RootH = I(3), Scale100 = p.Length > 4 ? Math.Max(50, I(4)) : 100 };
                    continue;
                }
                if (!bars.TryGetValue(n, out var bar) || p.Length < 6) continue;
                var box = new Box(I(2), I(3), I(4), I(5));
                if (box.Width < 1 || box.Height < 1) continue;
                switch (p[0])
                {
                    case "w": bar.Widgets = box; break;
                    case "s": bar.System = box; break;
                    case "a": bar.Apps = box; break;
                    case "i": bar.Icons.Add(box); break;
                }
            }
            foreach (var kv in bars.OrderBy(k => k.Key)) f.Bars.Add(kv.Value);
            return f;
        }
        catch { return null; }
    }

    public static GeometryFile? Read(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var r = new StreamReader(fs, Encoding.UTF8);
            return Parse(r.ReadToEnd());
        }
        catch { return null; }
    }
}
