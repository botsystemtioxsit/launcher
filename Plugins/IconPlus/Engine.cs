using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace IconPlusApp;

sealed record ApplyResult(int Done, int Failed, List<string> Messages);

// Применяет значки: перекрашивает исходные значки Windows в выбранный цвет или ставит значки из набора.
// Всё — в настройках текущего пользователя (HKCU и ярлыки пользователя); каждое прежнее значение запоминается в state.json.
sealed class IconEngine
{
    sealed record Sink(string Key, Func<string> Source, Action<string> Write);

    readonly IconState state;
    readonly RegistryWriter reg;
    MachineJob job = new();   // действия, для которых нужны права администратора, собираются сюда и выполняются разом

    public IconEngine() { Paths.Ensure(); state = StateStore.Load(); reg = new RegistryWriter(state); }
    public IconState State => state;
    public int ChangesCount => state.Reg.Count + state.Lnk.Count;

    // ---------- что куда пишется ----------
    IEnumerable<Sink> SinksFor(Target t)
    {
        switch (t.Kind)
        {
            case TargetKind.Folder:
                yield return new Sink("folder", FolderSource, p => job.FolderIco = p);
                break;
            case TargetKind.Drive:
                var type = t.Id switch
                {
                    "drive-fixed" => Native.DRIVE_FIXED,
                    "drive-removable" => Native.DRIVE_REMOVABLE,
                    "drive-optical" => Native.DRIVE_CDROM,
                    _ => Native.DRIVE_REMOTE,
                };
                foreach (var d in DriveInfo.GetDrives())
                {
                    var root = d.Name;                                   // «C:\»
                    if (root.Length < 2 || Native.GetDriveType(root) != type) continue;
                    var letter = root[0].ToString().ToUpperInvariant();
                    yield return new Sink("drive:" + letter, () => Native.IconLocation(root, false),
                        p => reg.Set(@"Software\Classes\Applications\Explorer.exe\Drives\" + letter + @"\DefaultIcon", "", p));
                }
                break;
            case TargetKind.Clsid:
                var clsid = t.Clsid!;
                var path = @"Software\Microsoft\Windows\CurrentVersion\Explorer\CLSID\" + clsid + @"\DefaultIcon";
                yield return new Sink("clsid:" + t.Id, () => ClsidSource(clsid, t.ValueName), p =>
                {
                    reg.Set(path, t.ValueName ?? "", p);
                    if (t.Id == "recycle-empty") reg.Set(path, "", p);   // значок «по умолчанию» у корзины — пустая
                });
                break;
            case TargetKind.FileType:
                foreach (var ext in t.Extensions!)
                {
                    var e = ext;
                    yield return new Sink("ext:" + e, () => { var a = Native.AssocDefaultIcon("." + e); return a.Length > 0 ? a : Native.IconLocation("x." + e, true); }, p =>
                    {
                        foreach (var progId in ProgIds(e)) reg.Set(@"Software\Classes\" + progId + @"\DefaultIcon", "", p);
                    });
                }
                break;
            case TargetKind.Shortcuts:
                foreach (var f in ShortcutFiles(t.Id))
                {
                    var file = f;
                    var machine = t.NeedsAdmin;
                    yield return new Sink("lnk:" + file, () => LnkSource(file), p => { if (machine) job.Lnks.Add(new MachineLnk { File = file, Ico = p }); else WriteLnk(file, p); });
                }
                break;
        }
    }

    static string FolderSource()
    {
        var v = ReadMachineIcon(@"SOFTWARE\Classes\Folder\DefaultIcon", "");
        if (v.Length > 0) return v;
        return Native.IconLocation("folder", true, directory: true);
    }

    static string ClsidSource(string clsid, string? valueName)
    {
        var v = ReadMachineIcon(@"SOFTWARE\Classes\CLSID\" + clsid + @"\DefaultIcon", valueName ?? "");
        if (v.Length == 0 && valueName != null) v = ReadMachineIcon(@"SOFTWARE\Classes\CLSID\" + clsid + @"\DefaultIcon", "");
        return v.Length > 0 ? v : Native.IconLocationOfShellItem("::" + clsid);
    }

    static string ReadMachineIcon(string path, string name)
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(path);
            return k?.GetValue(name, "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
        }
        catch { return ""; }
    }

    // Какими типами файлов (ProgID) «владеет» расширение: тот, что выбрал пользователь, и тот, что указан по умолчанию.
    static List<string> ProgIds(string ext)
    {
        var list = new List<string>();
        try
        {
            using (var uc = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\." + ext + @"\UserChoice"))
                if (uc?.GetValue("ProgId") is string p && p.Length > 0) list.Add(p);
            using (var k = Registry.ClassesRoot.OpenSubKey("." + ext))
                if (k?.GetValue("") is string p2 && p2.Length > 0 && !list.Contains(p2)) list.Add(p2);
        }
        catch { }
        return list;
    }

    static IEnumerable<string> ShortcutFiles(string targetId)
    {
        switch (targetId)
        {
            case "lnk-desktop":
                return Shortcuts.In(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), false);
            case "lnk-desktop-all":
                return Shortcuts.In(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), false);
            case "lnk-start":
                return Shortcuts.In(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"), true);
            case "lnk-start-all":
                return Shortcuts.In(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"), true);
            default:
                return Shortcuts.In(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"), false);
        }
    }

    static string LnkSource(string file)
    {
        if (Shortcuts.ReadIcon(file, out var p, out var i) && p.Length > 0) return Environment.ExpandEnvironmentVariables(p) + "," + i;
        // у ярлыка нет своего значка — берётся значок цели
        var target = Shortcuts.ReadTarget(file);
        if (target.Length > 0 && File.Exists(target))
        {
            var ext = Path.GetExtension(target).ToLowerInvariant();
            if (ext is ".exe" or ".dll" or ".ico" or ".icl") return target + ",0";
            var a = Native.AssocDefaultIcon(ext);
            if (a.Length > 0) return a;
        }
        return Native.IconLocation(file, false);
    }

    void WriteLnk(string file, string icoPath)
    {
        if (state.Lnk.All(c => c.File != file))
        {
            if (!Shortcuts.ReadIcon(file, out var op, out var oi)) throw new IOException("ярлык не читается");
            state.Lnk.Add(new LnkChange { File = file, OldPath = op, OldIndex = oi });
        }
        if (!Shortcuts.WriteIcon(file, icoPath, 0)) throw new IOException("ярлык не записывается");
    }

    // Откуда брать исходный значок. Запоминается при первом обращении, чтобы потом (когда значок уже заменён нами) красить именно оригинал.
    string SourceOf(string key, Func<string> query)
    {
        if (state.Sources.TryGetValue(key, out var v) && v.Length > 0) return v;
        v = query();
        if (!string.IsNullOrEmpty(v)) state.Sources[key] = v;
        return v;
    }

    // ---------- перекраска ----------
    public ApplyResult ApplyColor(IconSettings s, Action<string>? progress = null)
    {
        int done = 0, failed = 0;
        var msgs = new List<string>();
        job = new MachineJob();
        foreach (var id in s.Enabled)
        {
            var t = Target.ById(id);
            if (t == null) continue;
            progress?.Invoke(t.Title);
            foreach (var sink in SinksFor(t).ToList())
            {
                try
                {
                    var src = SourceOf(sink.Key, sink.Source);
                    if (src.Length == 0) { failed++; continue; }
                    var ico = GeneratedIcon(src, s);
                    if (ico == null) { failed++; msgs.Add("не достать значок: " + sink.Key); continue; }
                    sink.Write(ico);
                    done++;
                }
                catch (Exception ex) { failed++; msgs.Add(sink.Key + ": " + ex.Message); }
            }
        }
        FlushMachine(msgs, ref done, ref failed);
        state.Applied = "color";
        StateStore.Save(state);
        Native.NotifyIconsChanged();
        return new ApplyResult(done, failed, msgs);
    }

    static string? GeneratedIcon(string sourceLocation, IconSettings s)
    {
        var key = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(sourceLocation + "|" + s.Color + "|" + s.Style + "|" + s.Strength))).ToLowerInvariant()[..16];
        var path = Path.Combine(Paths.Generated, key + ".ico");
        if (File.Exists(path)) return path;
        var imgs = IconData.LoadAllSizes(sourceLocation, (px, _, _) => Tint.Apply(px, s.Color, s.Style, s.Strength));
        if (imgs == null) return null;
        IconData.WriteIco(path, imgs);
        return path;
    }

    // Превью для окна: исходный значок цели и его перекрашенная копия (размер 48).
    public (System.Windows.Media.Imaging.BitmapSource? Before, System.Windows.Media.Imaging.BitmapSource? After) Preview(Target t, IconSettings s)
    {
        var sink = SinksFor(t).FirstOrDefault();
        if (sink == null) return (null, null);
        var src = SourceOf(sink.Key, sink.Source);
        var before = src.Length > 0 ? IconData.Load(src, 48) : null;
        if (before == null) return (null, null);
        var after = IconData.FromPixels(Tint.Apply(IconData.Pixels(before), s.Color, s.Style, s.Strength), before.PixelWidth, before.PixelHeight);
        return (before, after);
    }

    // ---------- набор значков ----------
    // map: цель → файл .ico из набора
    public ApplyResult ApplyPack(Dictionary<string, string> map, Action<string>? progress = null)
    {
        int done = 0, failed = 0;
        var msgs = new List<string>();
        job = new MachineJob();
        foreach (var (id, file) in map)
        {
            var t = Target.ById(id);
            if (t == null || t.Kind == TargetKind.Shortcuts || !File.Exists(file)) continue;
            progress?.Invoke(t.Title);
            foreach (var sink in SinksFor(t).ToList())
            {
                try
                {
                    SourceOf(sink.Key, sink.Source);   // запомнить оригинал до замены
                    sink.Write(file);
                    done++;
                }
                catch (Exception ex) { failed++; msgs.Add(sink.Key + ": " + ex.Message); }
            }
        }
        FlushMachine(msgs, ref done, ref failed);
        state.Applied = "pack";
        StateStore.Save(state);
        Native.NotifyIconsChanged();
        return new ApplyResult(done, failed, msgs);
    }

    // Выполнить то, что требует прав администратора (одно окно «Разрешить?»).
    void FlushMachine(List<string> msgs, ref int done, ref int failed)
    {
        if (job.IsEmpty) return;
        var r = Machine.Run(job);
        if (r.Error.Length > 0) msgs.Add("для администратора: " + r.Error);
        if (job.FolderIco != null) { if (r.FolderOk) { done++; state.FolderMachine = true; } else failed++; }
        done += r.LnkOk; failed += r.LnkFailed;
        if (r.LnkOk > 0) state.MachineLnk = true;
        if (job.Lnks.Count > 0 && r.LnkOk == 0 && r.Error.Length > 0) failed += job.Lnks.Count - r.LnkFailed;
        job = new MachineJob();
    }

    // ---------- вернуть как было ----------
    public int RestoreAll()
    {
        var n = 0;
        foreach (var c in Enumerable.Reverse(state.Reg).ToList()) { RegistryWriter.Revert(c); n++; }
        if (state.FolderMachine || state.MachineLnk)
        {
            try { var r = Machine.Run(new MachineJob { Restore = true }); n += r.LnkOk + (r.FolderOk ? 1 : 0); } catch { }
            state.FolderMachine = false; state.MachineLnk = false;
        }
        foreach (var l in state.Lnk.ToList())
        {
            try { if (File.Exists(l.File) && Shortcuts.WriteIcon(l.File, l.OldPath, l.OldIndex)) n++; } catch { }
        }
        state.Reg.Clear(); state.Lnk.Clear(); state.Sources.Clear(); state.Applied = "";
        StateStore.Save(state);
        Native.NotifyIconsChanged();
        return n;
    }
}
