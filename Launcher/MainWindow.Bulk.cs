using System.Diagnostics;
using System.IO;
using System.Windows;

namespace BattleLauncher;

// «Обновить все» и «Запустить все»: общие кнопки в боковом меню (всё сразу) и в шапке каждой вкладки (только её список).
// scope: "all" — всё; "apps" — приложения; "style" / "net" — плагины раздела; "ext" — расширения для браузера.
public partial class MainWindow
{
    bool bulkBusy;

    void SetBulkEnabled(bool on) =>
        BulkUpdateBtn.IsEnabled = BulkLaunchBtn.IsEnabled = AppsUpdateAllBtn.IsEnabled = AppsLaunchAllBtn.IsEnabled = PluginsUpdateAllBtn.IsEnabled = PluginsLaunchAllBtn.IsEnabled = on;

    async void BulkUpdateBtn_Click(object sender, RoutedEventArgs e) => await UpdateAllAsync("all");
    async void BulkLaunchBtn_Click(object sender, RoutedEventArgs e) => await LaunchAllAsync("all");
    async void AppsUpdateAll_Click(object sender, RoutedEventArgs e) => await UpdateAllAsync("apps");
    async void AppsLaunchAll_Click(object sender, RoutedEventArgs e) => await LaunchAllAsync("apps");
    async void PluginsUpdateAll_Click(object sender, RoutedEventArgs e) => await UpdateAllAsync(currentView);   // style | net | ext
    async void PluginsLaunchAll_Click(object sender, RoutedEventArgs e) => await LaunchAllAsync(currentView);

    static bool InScope(string scope, string part) => scope == "all" || scope == part;
    static bool PluginInScope(string scope, AppDef d) => scope == "all" || Catalog.CategoryOf(d) == scope;

    // Обновляет всё, у чего вышла новая версия. Сначала всё ставится в очередь менеджера (видно, что за чем идёт),
    // потом очередь выполняет по одному: каждый плагин сам просит выйти, заменяется и запускается снова.
    async Task UpdateAllAsync(string scope)
    {
        if (bulkBusy) return;
        bulkBusy = true;
        SetBulkEnabled(false);
        try
        {
            Toast("Проверяю, что можно обновить…");
            // 1) читаем, что есть нового, — ничего ещё не трогая
            if (InScope(scope, "apps")) await LoadApps(true);
            if (InScope(scope, "style") || InScope(scope, "net")) await LoadPlugins(true);
            if (InScope(scope, "ext")) await LoadBrowserPlugins(true);

            // 2) всё найденное — в очередь одним махом
            var tasks = new List<Task<bool>>();
            if (InScope(scope, "apps"))
            {
                foreach (var a in apps.Where(x => x.Allowed).ToList())
                {
                    if (a.Installed != null && a.UpdateAvailable) tasks.Add(InstallApp(a, "Обновлено"));
                    if (a.Native != null && a.NativeUpdate) tasks.Add(InstallNative(a, "Обновлено"));
                }
            }
            if (InScope(scope, "style") || InScope(scope, "net"))
                foreach (var p in pluginInfos.Where(x => x.Native != null && x.NativeUpdate && PluginInScope(scope, x.Def)).ToList())
                    tasks.Add(InstallPlugin(p, "Обновлено"));
            if (InScope(scope, "ext"))
                foreach (var b in browserInfos.Where(x => x.Update).ToList())
                    tasks.Add(InstallBrowserPlugin(b, "Обновлено"));

            // 3) ждём, пока очередь всё выполнит
            var results = await Task.WhenAll(tasks);
            var need = results.Length;
            var done = results.Count(r => r);
            Toast(need == 0 ? "Всё уже самое новое"
                : done == need ? $"Обновлено: {done}"
                : $"Обновлено {done} из {need}. Что не вышло — причина в менеджере внизу и в карточке.");
        }
        catch (Exception ex) { Toast("Не получилось: " + ex.Message); }
        finally { bulkBusy = false; SetBulkEnabled(true); }
    }

    static bool AnyRunning(string exe)
    {
        try { return Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)).Length > 0; }
        catch { return false; }
    }

    // Запускает всё установленное, что сейчас не работает. Плагины с фоновым режимом — тихо (--background), остальные — открывается их окно.
    async Task LaunchAllAsync(string scope)
    {
        if (bulkBusy) return;
        bulkBusy = true;
        SetBulkEnabled(false);
        try
        {
            int started = 0, already = 0, failed = 0;
            if (InScope(scope, "apps"))
            {
                if (apps.Count == 0) await LoadApps(false);
                foreach (var a in apps.Where(x => x.Allowed).ToList())
                {
                    try
                    {
                        if (a.Installed != null)
                        {
                            if (!await RequireAllowed(a.Def)) continue;
                            var found = await Task.Run(() => Installer.FindInstalled(a.Def.ProductName));
                            if (found == null) continue;
                            if (AnyRunning(found.Exe)) { already++; continue; }
                            Installer.Launch(found.Exe);
                            started++;
                            OpsNote("Запуск", a.Def.Name, true, "Запущено");
                        }
                        else if (a.Native != null)
                        {
                            var nat = await Task.Run(() => NativeInstaller.Find(a.Def));
                            if (nat == null) continue;
                            if (AnyRunning(nat.Exe)) { already++; continue; }
                            NativeInstaller.Launch(nat);
                            started++;
                            OpsNote("Запуск", a.Def.NativeName ?? a.Def.Name, true, "Запущено");
                        }
                    }
                    catch (Exception ex) { failed++; OpsNote("Запуск", a.Def.Name, false, ex.Message); }
                }
            }
            if (InScope(scope, "style") || InScope(scope, "net"))
            {
                if (pluginInfos.Count == 0) await LoadPlugins(false);
                foreach (var p in pluginInfos.Where(x => x.Native != null && PluginInScope(scope, x.Def)).ToList())
                {
                    try
                    {
                        if (NativeInstaller.IsRunning(p.Native!.Exe)) { already++; continue; }
                        NativeInstaller.Relaunch(p.Def, p.Native.Exe);
                        started++;
                        OpsNote("Запуск", p.Def.Name, true, "Запущено");
                    }
                    catch (Exception ex) { failed++; OpsNote("Запуск", p.Def.Name, false, ex.Message); }
                }
            }
            Toast(started == 0 && failed == 0
                ? (already > 0 ? "Всё установленное уже запущено" : "Нечего запускать: пока ничего не установлено")
                : $"Запущено: {started}" + (already > 0 ? $" · уже работали: {already}" : "") + (failed > 0 ? $" · не вышло: {failed}" : ""));
        }
        catch (Exception ex) { Toast("Не получилось: " + ex.Message); }
        finally { bulkBusy = false; SetBulkEnabled(true); }
    }
}
