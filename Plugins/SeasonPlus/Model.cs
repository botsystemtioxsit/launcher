using System.IO;
using System.Text.Json;

namespace SeasonPlusApp;

// Что выбрал человек. Файл: %APPDATA%\BattleLauncher\season-plus-settings.json
sealed class SeasonSettings
{
    public string Season { get; set; } = "auto";        // auto (по календарю) | winter | spring | summer | autumn | off
    public string Holiday { get; set; } = "auto";       // off | auto (по датам) | newyear | xmas | tet | halloween | valentine | women | maslenitsa | easter | custom
    public int Intensity { get; set; } = 45;            // 5–100: сколько всего падает
    public int Wind { get; set; } = 10;                 // −100…100: куда и как сильно дует
    public int Speed { get; set; } = 100;               // 50–200 %
    public int Size { get; set; } = 100;                // 50–200 %
    public bool Piles { get; set; } = true;             // сугробы, листва, трава на окнах
    public int PileHeight { get; set; } = 50;           // 10–100: насколько высокие
    public bool TaskbarPile { get; set; } = true;       // и на панели задач
    public bool Props { get; set; } = true;             // украшения на окнах (тыквы, подарки, яйца…)
    public int Garland { get; set; } = 1;               // 0 никогда, 1 в праздничные дни, 2 всегда
    public int GarlandWhere { get; set; } = 0;          // 0 окна и панель, 1 только панель задач
    public int GarlandMode { get; set; } = 0;           // 0 переливы, 1 бегущие огни, 2 мерцание, 3 ровный свет, 4 вспышки, 5 смена цвета
    public int GarlandColor { get; set; } = 0;          // 0 разноцветная, 1 тёплая, 2 холодная, 3 синяя, 4 красно-зелёная, 5 золотая
    public int GarlandBulb { get; set; } = 0;           // 0 лампочки, 1 шарики, 2 звёздочки, 3 бахрома
    public bool Countdown { get; set; } = true;         // отсчёт и поздравление в новогоднюю ночь
    public bool NyFireworks { get; set; } = true;       // салют в полночь
    public int CustomDay { get; set; }                  // свой праздник: день и месяц (0 — не задан)
    public int CustomMonth { get; set; }
    public int CustomFx { get; set; }                   // 0 шарики, 1 конфетти, 2 цветы, 3 салют
    public string CustomText { get; set; } = "";
    public bool Active { get; set; }                    // включено: плагин остаётся в фоне и запускается вместе с Windows

    static readonly string[] Seasons = { "auto", "winter", "spring", "summer", "autumn", "off" };
    static readonly string[] Holidays = { "off", "auto", "newyear", "xmas", "tet", "halloween", "valentine", "women", "maslenitsa", "easter", "custom" };

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BattleLauncher");
    static string PathFile => Path.Combine(Dir, "season-plus-settings.json");

    public void Normalize()
    {
        if (!Seasons.Contains(Season)) Season = "auto";
        if (!Holidays.Contains(Holiday)) Holiday = "auto";
        Intensity = Math.Clamp(Intensity, 5, 100);
        Wind = Math.Clamp(Wind, -100, 100);
        Speed = Math.Clamp(Speed, 50, 200);
        Size = Math.Clamp(Size, 50, 200);
        PileHeight = Math.Clamp(PileHeight, 10, 100);
        Garland = Math.Clamp(Garland, 0, 2);
        GarlandWhere = Math.Clamp(GarlandWhere, 0, 1);
        GarlandMode = Math.Clamp(GarlandMode, 0, 5);
        GarlandColor = Math.Clamp(GarlandColor, 0, 5);
        GarlandBulb = Math.Clamp(GarlandBulb, 0, 3);
        CustomDay = Math.Clamp(CustomDay, 0, 31);
        CustomMonth = Math.Clamp(CustomMonth, 0, 12);
        CustomFx = Math.Clamp(CustomFx, 0, 3);
        CustomText = new string((CustomText ?? "").Where(ch => !char.IsControl(ch)).Take(40).ToArray());
    }

    public static SeasonSettings Load()
    {
        try
        {
            var s = JsonSerializer.Deserialize<SeasonSettings>(File.ReadAllText(PathFile)) ?? new SeasonSettings();
            s.Normalize();
            return s;
        }
        catch { return new SeasonSettings(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = PathFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this));
            File.Move(tmp, PathFile, true);
        }
        catch { }
    }

    public SeasonSettings Clone() => (SeasonSettings)MemberwiseClone();
}

// Календарь: какое сейчас время года и какой праздник.
static class Cal
{
    public static readonly string[] SeasonIds = { "winter", "spring", "summer", "autumn" };

    // 0 зима, 1 весна, 2 лето, 3 осень; −1 — выключено.
    public static int SeasonIndex(SeasonSettings s, DateTime now)
    {
        var i = Array.IndexOf(SeasonIds, s.Season);
        if (i >= 0) return i;
        if (s.Season == "off") return -1;
        return now.Month switch { 12 or 1 or 2 => 0, 3 or 4 or 5 => 1, 6 or 7 or 8 => 2, _ => 3 };
    }

    // Православная Пасха (по юлианскому календарю + 13 дней — верно для 1900–2099).
    public static DateTime Easter(int year)
    {
        int a = year % 4, b = year % 7, c = year % 19, d = (19 * c + 15) % 30, e = (2 * a + 4 * b - d + 34) % 7;
        int month = (d + e + 114) / 31, day = (d + e + 114) % 31 + 1;
        return new DateTime(year, month, day).AddDays(13);
    }

    // Идентификатор праздника на сегодня или "" (ничего).
    public static string HolidayOf(SeasonSettings s, DateTime now)
    {
        if (s.Holiday == "off") return "";
        if (s.Holiday != "auto") return s.Holiday;
        var d = now.Date; int m = d.Month, day = d.Day;
        if ((m == 12 && day >= 15) || (m == 1 && day <= 10)) return "newyear";
        if ((m == 10 && day >= 25) || (m == 11 && day == 1)) return "halloween";
        if (m == 2 && day >= 12 && day <= 14) return "valentine";
        if (m == 3 && day >= 6 && day <= 8) return "women";
        var e = Easter(d.Year);
        if (d >= e.AddDays(-55) && d <= e.AddDays(-49)) return "maslenitsa";
        if (d >= e.AddDays(-3) && d <= e.AddDays(1)) return "easter";
        if (s.CustomDay > 0 && s.CustomMonth > 0 && m == s.CustomMonth && day == s.CustomDay) return "custom";
        return "";
    }
}

// Верхний край окна (или панели задач), на который падает снег. Left/Right/Top — настоящие пиксели экрана.
sealed class Surf
{
    public long Id;
    public double Left, Right, Top, Radius;
    public bool Taskbar;
    public List<(double A, double B)> Vis = new();   // видимые куски края (не закрытые окнами выше)
}
