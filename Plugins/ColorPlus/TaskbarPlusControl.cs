using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ColorPlusApp;

// Наша панель — это Taskbar Plus, и красит её его «Новый движок». У владельца панель оставалась серой, потому что Taskbar Plus был запущен, а «Новый движок»
// в нём выключен (панель резалась обычным способом и цвет получить не могла). Color Plus теперь сам видит такое состояние и при «Применить»
// включает движок: в настройках Taskbar Plus ставится «Новый движок», плагин перезапускается (на несколько секунд) и подключается к панели.
static class TaskbarPlusControl
{
    public enum State { NotInstalled, NotRunning, EngineOff, Starting, Ready }

    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BattleLauncher");
    static readonly string SettingsPath = Path.Combine(Dir, "taskbar-plus-settings.json");
    static string ExePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "TaskbarPlus", "TaskbarPlus.exe");
    const string QuitEvent = "GhostShell.Plugin.Quit.taskbar-plus";

    static bool Running() { try { return Process.GetProcessesByName("TaskbarPlus").Length > 0; } catch { return false; } }

    static bool EngineSetting()
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            return doc.RootElement.TryGetProperty("UseXaml", out var u) && u.ValueKind == JsonValueKind.True;
        }
        catch { return false; }
    }

    public static State Get()
    {
        var running = Running();
        if (!running && !File.Exists(ExePath)) return State.NotInstalled;
        if (!running) return State.NotRunning;
        // «готов» — только если движок включён в самом Taskbar Plus: старая копия движка может висеть в Проводнике и писать статус после выхода плагина
        if (!EngineSetting()) return State.EngineOff;
        return TaskbarLink.EngineState().Ready ? State.Ready : State.Starting;
    }

    public static string Text(State s) => s switch
    {
        State.NotInstalled => "Taskbar Plus не установлен — поставьте его на вкладке «Плагины» лаунчера: без него нашу панель не перекрасить.",
        State.NotRunning => "Taskbar Plus не запущен. При «Применить» Color Plus сам запустит его и включит «Новый движок».",
        State.EngineOff => "Taskbar Plus запущен, но «Новый движок» в нём выключен — поэтому панель серая. При «Применить» Color Plus включит его сам (Taskbar Plus перезапустится на несколько секунд).",
        State.Starting => "Новый движок Taskbar Plus подключается к панели — это занимает до минуты.",
        _ => "Taskbar Plus работает: его острова получат цвет.",
    };

    // Довести дело до рабочего движка: включить «Новый движок», перезапустить Taskbar Plus, дождаться подключения. Возвращает текст проблемы или null.
    public static async Task<string?> EnsureEngineAsync(Action<string>? progress = null)
    {
        var st = Get();
        if (st == State.Ready) return null;
        if (st == State.NotInstalled) return Text(st);
        if (Environment.OSVersion.Version.Build < 22000) return "Новый движок Taskbar Plus работает только в Windows 11.";
        try
        {
            if (st is State.EngineOff or State.NotRunning)
            {
                progress?.Invoke("Включаю «Новый движок» в Taskbar Plus…");
                await Task.Run(() =>
                {
                    if (Running())
                    {
                        try { using var q = EventWaitHandle.OpenExisting(QuitEvent); q.Set(); } catch { }
                        for (var i = 0; i < 100 && Running(); i++) Thread.Sleep(100);
                        if (Running()) throw new InvalidOperationException("Taskbar Plus не закрылся — закройте его и нажмите «Применить» ещё раз.");
                    }
                    SetEngineOn();
                    if (File.Exists(ExePath)) Process.Start(new ProcessStartInfo(ExePath, "--background") { UseShellExecute = true });
                });
            }
            for (var i = 0; i < 90; i++)
            {
                if (TaskbarLink.EngineState().Ready) return null;
                if (i % 5 == 0) progress?.Invoke("Жду, пока движок Taskbar Plus подключится к панели… (" + i + " с)");
                await Task.Delay(1000);
            }
            return "Движок Taskbar Plus не подключился за 90 секунд. Откройте Taskbar Plus и посмотрите его состояние.";
        }
        catch (Exception ex) { return ex.Message; }
    }

    static void SetEngineOn()
    {
        Directory.CreateDirectory(Dir);
        JsonObject node;
        try { node = JsonNode.Parse(File.ReadAllText(SettingsPath)) as JsonObject ?? new JsonObject(); } catch { node = new JsonObject(); }
        node["UseXaml"] = true;
        node["XamlConsentShown"] = true;   // владелец уже соглашался на движок; предупреждение при запуске не нужно
        var tmp = SettingsPath + ".tmp";
        File.WriteAllText(tmp, node.ToJsonString());
        File.Move(tmp, SettingsPath, true);
    }
}
