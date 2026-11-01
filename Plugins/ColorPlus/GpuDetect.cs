using System.Collections.Concurrent;
using System.Diagnostics;

namespace ColorPlusApp;

// Игры и программы, которые сами рисуют окно через видеокарту (OpenGL, Vulkan, DirectX 12): Minecraft, игры Steam, движки Unity и Unreal.
// У них прозрачный слой окна — размытие, акрил, «слюда» и цветовой ключ показываются сквозь него и закрашивают картинку (мир Minecraft становился сплошным
// розовым). Такие окна Color Plus не трогает никогда: ни общими настройками, ни по правилу программы.
static class GpuDetect
{
    // Классы окон игровых библиотек и движков (GLFW — Minecraft, SDL, Unity, Unreal, Source, Godot…)
    static readonly string[] ClassPrefixes = { "GLFW", "SDL_app", "SDL_Window", "UnityWndClass", "UnityContainerWndClass", "UnrealWindow", "Valve001", "LWJGL", "CryENGINE", "SFML_Window", "Allegro", "OGRE", "FNA" };
    static readonly HashSet<string> ClassExact = new(StringComparer.OrdinalIgnoreCase) { "Engine", "GodotEngine", "RGSS Player", "OpenGL Window" };

    // Модули, которые подгружают именно игры и 3D-программы
    static readonly HashSet<string> Markers = new(StringComparer.OrdinalIgnoreCase)
    {
        "opengl32.dll", "vulkan-1.dll", "d3d12.dll", "d3d12core.dll", "steam_api.dll", "steam_api64.dll", "unityplayer.dll", "sdl2.dll", "sdl3.dll",
        "glfw.dll", "glfw3.dll", "lwjgl.dll", "lwjgl64.dll", "lwjgl_opengl.dll", "lwjgl_glfw.dll", "discord_game_sdk.dll", "eossdk-win64-shipping.dll",
    };

    // Папки, где лежат установленные игры
    static readonly string[] GamePaths =
    {
        @"\steamapps\common\", @"\epic games\", @"\gog galaxy\games\", @"\gog games\", @"\riot games\", @"\ubisoft game launcher\games\",
        @"\ea games\", @"\origin games\", @"\xboxgames\", @"\games\",
    };

    // Браузеры подгружают графические модули ради страниц, а не игр: их окна обрабатываются по обычным правилам
    static readonly HashSet<string> Safe = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "brave", "opera", "opera_gx", "vivaldi", "msedgewebview2", "iexplore", "explorer", "windowsterminal", "code", "discord", "slack", "telegram", "notepad", "mspaint",
    };

    sealed record Info(DateTime At, string? Reason);
    static readonly ConcurrentDictionary<uint, Info> cache = new();

    // null — окно обычное; иначе короткая причина, почему его трогать нельзя.
    public static string? Why(string cls, uint pid, string proc)
    {
        foreach (var p in ClassPrefixes) if (cls.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return "игра или 3D-программа (" + cls + ") — окно не трогаю";
        if (ClassExact.Contains(cls)) return "игра или 3D-программа (" + cls + ") — окно не трогаю";

        if (cache.TryGetValue(pid, out var c) && DateTime.UtcNow - c.At < TimeSpan.FromSeconds(30)) return c.Reason;
        var reason = Scan(pid, proc);
        cache[pid] = new Info(DateTime.UtcNow, reason);
        if (cache.Count > 400) foreach (var k in cache.Where(kv => DateTime.UtcNow - kv.Value.At > TimeSpan.FromMinutes(5)).Select(kv => kv.Key).ToList()) cache.TryRemove(k, out _);
        return reason;
    }

    static string? Scan(uint pid, string proc)
    {
        try
        {
            using var p = Process.GetProcessById((int)pid);
            try
            {
                var path = p.MainModule?.FileName?.ToLowerInvariant() ?? "";
                foreach (var g in GamePaths) if (path.Contains(g)) return "игра (лежит в папке игр) — окно не трогаю";
            }
            catch { /* путь не прочитать (защищённый процесс) — смотрим модули */ }
            if (Safe.Contains(proc)) return null;
            try
            {
                foreach (ProcessModule m in p.Modules)
                    if (Markers.Contains(m.ModuleName ?? "")) return "программа рисует через видеокарту (" + m.ModuleName + ") — окно не трогаю";
            }
            catch { /* модули недоступны — считаем обычной программой */ }
        }
        catch { }
        return null;
    }
}
