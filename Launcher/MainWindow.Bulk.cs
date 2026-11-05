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

    // Обновляет всё, у чего вышла новая версия. По одному, по очереди: каждый плагин сам просит выйти, заменяется и запускается снова.
    async Task UpdateAllAsync(string scope)
    {
        if (bulkBusy) return;
        bulkBusy = true;
        SetBulkEnabled(false);
        try
        {
            Toast("Проверяю, что можно обновить…");
            int need = 0, done = 0;
            if (InScope(scope, "apps"))
            {
                await LoadApps(true);
                foreach (var a in apps.Where(x => x.Allowed).ToList())
                {
                    if (a.Installed != null && a.UpdateAvailable) { need++; if (await InstallApp(a, "Обновлено")) done++; }
                    if (a.Native != null && a.NativeUpdate) { need++; if (await InstallNative(a, "Обновлено")) done++; }
                }
            }
            if (InScope(scope, "style") || InScope(scope, "net"))
            {
                await LoadPlugins(true);
                foreach (var p in pluginInfos.Where(x => x.Native != null && x.NativeUpdate && PluginInScope(scope, x.Def)).ToList())
                {
                    need++;
                    if (await InstallPlugin(p, "Обновлено")) done++;
                }
            }
            if (InScope(scope, "ext"))
            {
                await LoadBrowserPlugins(true);
                foreach (var b in browserInfos.Where(x => x.Update).ToList())
                {
                    need++;
                    if (await InstallBrowserPlugin(b, "Обновлено")) done++;
                }
            }
            Toast(need == 0 ? "Всё уже самое новое"
                : done == need ? $"Обновлено: {done}"
                : $"Обновлено {done} из {need}. Что не вышло — причина в карточке.");
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
                        }
                        else if (a.Native != null)
                        {
                            var nat = await Task.Run(() => NativeInstaller.Find(a.Def));
                            if (nat == null) continue;
                            if (AnyRunning(nat.Exe)) { already++; continue; }
                            NativeInstaller.Launch(nat);
                            started++;
                        }
                    }
                    catch { failed++; }
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
                    }
                    catch { failed++; }
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
