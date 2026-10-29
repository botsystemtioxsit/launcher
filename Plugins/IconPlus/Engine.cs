using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace IconPlusApp;

// Один результат: к какой группе относится, о чём речь и чем кончилось (ok или причина).
sealed record Outcome(string Group, string Item, string Result)
{
    public bool Ok => Result == "ok";
}

sealed record ApplyResult(int Done, int Failed, List<string> Messages, List<Outcome> Outcomes)
{
    public Dictionary<string, string> Aumids { get; init; } = new();   // ярлык → идентификатор программы (для отчёта)
}

// Применяет значки: перекрашивает исходные значки Windows в выбранный цвет или ставит значки из набора.
// Всё — в настройках текущего пользователя (HKCU и ярлыки пользователя); каждое прежнее значение запоминается в state.json.
sealed class IconEngine
{
    sealed record Sink(string Key, Func<string> Source, Action<string> Write, Func<string>? Fallback = null);

    readonly IconState state;
    readonly RegistryWriter reg;
    MachineJob job = new();   // действия, для которых нужны права администратора, собираются сюда и выполняются разом

    public IconEngine() { Paths.Ensure(); state = StateStore.Load(); reg = new RegistryWriter(state); }
    public IconState State => state;
    public int ChangesCount => state.Reg.Count + state.Lnk.Count + state.Ini.Count + state.Created.Count;

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
                    }, () => ShellSample(e));
                }
                break;
            case TargetKind.Shortcuts:
                foreach (var f in ShortcutFiles(t.Id).Where(x => !state.Created.Contains(x, StringComparer.OrdinalIgnoreCase)))
                {
                    var file = f;
                    var machine = t.NeedsAdmin;
                    yield return new Sink("lnk:" + file, () => LnkSource(file), p => { if (machine) job.Lnks.Add(new MachineLnk { File = file, Ico = p }); else WriteLnk(file, p); },
                        () => Shortcuts.IsUrl(file) ? "shell:" + file : "lnktarget:" + file);
                }
                break;
            case TargetKind.KnownFolders:
                foreach (var (name, folder) in KnownFolders())
                {
                    var ini = Path.Combine(folder, "desktop.ini");
                    var fpath = folder;
                    yield return new Sink("kf:" + name, () => "shell:" + fpath, p => WriteIni(ini, p));
                }
                break;
            case TargetKind.StartApps:
                foreach (var a in StartApps.Candidates(state.Created))
                {
                    var app = a;
                    appNames["app:" + app.AppId] = app.Name;
                    yield return new Sink("app:" + app.AppId, () => "shell:" + StartApps.AppsFolderPath(app.AppId), p => WriteStartApp(app, p));
                }
                break;
            case TargetKind.AllFileTypes:
                foreach (var (progId, icon) in AllProgIdIcons())
                {
                    var pid = progId; var ic = icon;
                    yield return new Sink("prog:" + pid, () => ic, p => reg.Set(@"Software\Classes\" + pid + @"\DefaultIcon", "", p));
                }
                break;
        }
    }

    // Пустой образец файла с нужным расширением в папке Temp: Проводник нарисует для него значок типа (так, как он его показывает на самом деле).
    static string ShellSample(string ext)
    {
        var dir = Path.Combine(Path.GetTempPath(), "iconplus-ext");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "sample." + ext);
        if (!File.Exists(file)) File.WriteAllBytes(file, Array.Empty<byte>());
        return "shell:" + file;
    }

    readonly Dictionary<string, string> appNames = new();   // ключ приложения «Пуска» → его имя (для отчёта)
    readonly List<StartApp> createdNow = new();              // ярлыки, созданные в этот раз (для проверки на дубли)

    // Свой ярлык приложения «Пуска» в меню «Пуск» пользователя; уже созданный нами — только меняем значок.
    void WriteStartApp(StartApp app, string ico)
    {
        var file = Path.Combine(StartApps.ProgramsFolder, StartApps.FileName(app.Name) + ".lnk");
        if (File.Exists(file))
        {
            if (!state.Created.Contains(file, StringComparer.OrdinalIgnoreCase)) throw new IOException("в «Пуске» уже есть ярлык с таким именем");
            if (!Shortcuts.WriteIcon(file, ico, 0)) throw new IOException("ярлык не записывается");
            return;
        }
        Directory.CreateDirectory(StartApps.ProgramsFolder);
        StartApps.CreateLink(file, app.AppId, ico);
        state.Created.Add(file);
        createdNow.Add(app);
    }

    // Известные папки пользователя, значок которых хранится в desktop.ini.
    static IEnumerable<(string Name, string Path)> KnownFolders()
    {
        var list = new List<(string, string)>
        {
            ("Рабочий стол", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            ("Документы", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            ("Изображения", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
            ("Музыка", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)),
            ("Видео", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
            ("Загрузки", Native.KnownFolder(new Guid("374DE290-123F-4565-9164-39C4925E467B"))),
        };
        return list.Where(x => x.Item2.Length > 0 && Directory.Exists(x.Item2));
    }

    // Все типы файлов Windows, у которых есть свой значок (кроме тех, чей значок — сам файл: «%1»).
    static List<(string ProgId, string Icon)> AllProgIdIcons()
    {
        var list = new List<(string, string)>();
        try
        {
            foreach (var name in Registry.ClassesRoot.GetSubKeyNames())
            {
                if (name.Length == 0 || name[0] == '.' || name is "*" or "CLSID" or "Folder" or "Directory" or "Drive" or "Applications" or "AllFilesystemObjects" or "Unknown") continue;
                using var k = Registry.ClassesRoot.OpenSubKey(name + @"\DefaultIcon");
                var v = k?.GetValue("", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                if (string.IsNullOrWhiteSpace(v) || v.Contains("%1") || v.StartsWith("@")) continue;
                // уже перекрашено нами в этот раз — не трогаем второй раз
                using (var ours = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + name + @"\DefaultIcon"))
                    if (ours?.GetValue("") is string ov && ov.StartsWith(Paths.Generated, StringComparison.OrdinalIgnoreCase)) continue;
                list.Add((name, Environment.ExpandEnvironmentVariables(v)));
                if (list.Count >= 2500) break;
            }
        }
        catch { }
        return list;
    }

    void WriteIni(string file, string ico)
    {
        if (state.Ini.All(c => c.File != file)) state.Ini.Add(new IniChange { File = file, Old = DesktopIni.Read(file, "IconResource") });
        if (!DesktopIni.Write(file, "IconResource", ico + ",0")) throw new IOException("desktop.ini не записывается");
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
        if (Shortcuts.IsUrl(file))
        {
            if (Shortcuts.ReadIcon(file, out var up, out var ui) && up.Length > 0 && File.Exists(Environment.ExpandEnvironmentVariables(up))) return Environment.ExpandEnvironmentVariables(up) + "," + ui;
            return "shell:" + file;
        }
        if (Shortcuts.ReadIcon(file, out var p, out var i) && p.Length > 0)
        {
            var full = Environment.ExpandEnvironmentVariables(p);
            if (File.Exists(full)) return full + "," + i;
        }
        // у ярлыка нет своего значка — берётся значок цели
        var target = Shortcuts.ReadTarget(file);
        if (target.Length > 0 && File.Exists(target))
        {
            var ext = Path.GetExtension(target).ToLowerInvariant();
            if (ext is ".exe" or ".dll" or ".ico" or ".icl") return target + ",0";
            var a = Native.AssocDefaultIcon(ext);
            if (a.Length > 0) return a;
        }
        return "";   // дальше сработает запасной путь: значок цели в том виде, как его рисует Проводник
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
        if (v.StartsWith("shell:", StringComparison.Ordinal) || v.StartsWith("lnktarget:", StringComparison.Ordinal)) v = CaptureOriginal(v) ?? "";
        if (!string.IsNullOrEmpty(v)) state.Sources[key] = v;
        return v;
    }

    // Значок, который рисует Проводник (а не лежит в файле), запоминаем как файл — после замены Проводник покажет уже наш значок, а красить надо оригинал.
    static string? CaptureOriginal(string location)
    {
        var key = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(location))).ToLowerInvariant()[..16];
        var path = Path.Combine(Paths.Originals, key + ".ico");
        if (File.Exists(path)) return path;
        var imgs = IconData.LoadAllSizes(location);
        if (imgs == null) return null;
        IconData.WriteIco(path, imgs);
        return path;
    }

    // ---------- перекраска ----------
    public ApplyResult ApplyColor(IconSettings s, Action<string>? progress = null)
    {
        int done = 0, failed = 0;
        var msgs = new List<string>();
        var outcomes = new List<Outcome>();
        var aumids = new Dictionary<string, string>();
        job = new MachineJob();
        foreach (var id in s.Enabled)
        {
            var t = Target.ById(id);
            if (t == null) continue;
            progress?.Invoke(t.Title);
            foreach (var sink in SinksFor(t).ToList())
            {
                var item = ItemName(sink.Key);
                if (t.Kind == TargetKind.Shortcuts && sink.Key.StartsWith("lnk:", StringComparison.Ordinal))
                {
                    var au = Shortcuts.ReadAumid(sink.Key[4..]);
                    if (au.Length > 0 && aumids.Count < 400) aumids[item] = au;
                }
                try
                {
                    string? ico = null;
                    var src = SourceOf(sink.Key, sink.Source);
                    if (src.Length > 0) ico = GeneratedIcon(src, s);
                    if (ico == null && sink.Fallback != null)
                    {
                        var fb = sink.Fallback();
                        var captured = fb.StartsWith("shell:", StringComparison.Ordinal) || fb.StartsWith("lnktarget:", StringComparison.Ordinal) ? CaptureOriginal(fb) : fb;
                        if (captured != null) ico = GeneratedIcon(captured, s);
                        if (ico != null) state.Sources[sink.Key] = captured!;
                    }
                    if (ico == null) { failed++; outcomes.Add(new Outcome(t.Title, item, src.Length == 0 ? "значок не найден" : "значок не читается")); continue; }
                    sink.Write(ico);
                    done++;
                    if (!(t.NeedsAdmin && t.Kind is TargetKind.Shortcuts or TargetKind.Folder)) outcomes.Add(new Outcome(t.Title, item, "ok"));
                    else pendingMachine.Add((sink.Key, t.Title, item));
                }
                catch (Exception ex) { failed++; outcomes.Add(new Outcome(t.Title, item, ex.Message)); msgs.Add(sink.Key + ": " + ex.Message); }
            }
        }
        CheckDuplicates(msgs, outcomes, ref done, ref failed);
        FlushMachine(msgs, outcomes, ref done, ref failed);
        state.Applied = "color";
        StateStore.Save(state);
        Native.NotifyIconsChanged();
        return new ApplyResult(done, failed, msgs, outcomes) { Aumids = aumids };
    }

    // «Пуск» должен склеить наш ярлык с самим приложением. Если на этой версии Windows он показывает обе записи — наши ярлыки убираем (лучше без значка, чем с дублем).
    void CheckDuplicates(List<string> msgs, List<Outcome> outcomes, ref int done, ref int failed)
    {
        if (createdNow.Count == 0) return;
        var mine = createdNow.ToList();
        createdNow.Clear();
        System.Threading.Thread.Sleep(2500);
        var bad = StartApps.Duplicates(mine);
        if (bad.Count == 0) return;
        foreach (var app in mine.Where(a => bad.Contains(a.AppId, StringComparer.OrdinalIgnoreCase)))
        {
            var file = Path.Combine(StartApps.ProgramsFolder, StartApps.FileName(app.Name) + ".lnk");
            try { File.Delete(file); } catch { }
            state.Created.RemoveAll(x => string.Equals(x, file, StringComparison.OrdinalIgnoreCase));
            var i = outcomes.FindIndex(o => o.Item == app.Name && o.Group == Target.ById("start-apps")!.Title && o.Ok);
            if (i >= 0) outcomes[i] = new Outcome(outcomes[i].Group, app.Name, "«Пуск» показывает приложение дважды — ярлык убран");
            done--; failed++;
        }
        msgs.Add($"«Пуск» показал {bad.Count} приложений дважды — их ярлыки убраны");
    }

    // Краткое имя пункта для отчёта: у ярлыков — имя файла, у остального — сам ключ.
    string ItemName(string key) => key.StartsWith("lnk:", StringComparison.Ordinal) ? Path.GetFileNameWithoutExtension(key[4..]) : appNames.TryGetValue(key, out var n) ? n : key;

    // Пункты, которые ждут ответа от действия администратора (ключ → группа и имя)
    readonly List<(string Key, string Group, string Item)> pendingMachine = new();

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
        if (t.Kind == TargetKind.StartApps) return (null, null);   // список приложений «Пуска» долго собирается — превью не нужно
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
        var outcomes = new List<Outcome>();
        job = new MachineJob();
        foreach (var (id, file) in map)
        {
            var t = Target.ById(id);
            if (t == null || t.Keywords.Length == 0 || !File.Exists(file)) continue;
            progress?.Invoke(t.Title);
            foreach (var sink in SinksFor(t).ToList())
            {
                try
                {
                    SourceOf(sink.Key, sink.Source);   // запомнить оригинал до замены
                    sink.Write(file);
                    done++;
                    if (t.Kind == TargetKind.Folder) pendingMachine.Add((sink.Key, t.Title, ItemName(sink.Key))); else outcomes.Add(new Outcome(t.Title, ItemName(sink.Key), "ok"));
                }
                catch (Exception ex) { failed++; outcomes.Add(new Outcome(t.Title, ItemName(sink.Key), ex.Message)); msgs.Add(sink.Key + ": " + ex.Message); }
            }
        }
        FlushMachine(msgs, outcomes, ref done, ref failed);
        state.Applied = "pack";
        StateStore.Save(state);
        Native.NotifyIconsChanged();
        return new ApplyResult(done, failed, msgs, outcomes);
    }

    // Выполнить то, что требует прав администратора (одно окно «Разрешить?»).
    void FlushMachine(List<string> msgs, List<Outcome> outcomes, ref int done, ref int failed)
    {
        var pending = pendingMachine.ToList();
        pendingMachine.Clear();
        if (job.IsEmpty) return;
        var r = Machine.Run(job);
        if (r.Error.Length > 0) msgs.Add("для администратора: " + r.Error);
        var bad = r.Failures.ToDictionary(x => x.File, x => x.Reason, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, group, item) in pending)
        {
            var file = key.StartsWith("lnk:", StringComparison.Ordinal) ? key[4..] : "";
            string result;
            if (key == "folder") result = r.FolderOk ? "ok" : (r.Error.Length > 0 ? r.Error : "значок папок не изменён");
            else if (r.Error.Length > 0 && r.LnkOk == 0) result = r.Error;
            else if (bad.TryGetValue(file, out var why)) result = why;
            else result = "ok";
            outcomes.Add(new Outcome(group, item, result));
            if (result != "ok") { done--; failed++; }
        }
        if (job.FolderIco != null && r.FolderOk) state.FolderMachine = true;
        if (r.LnkOk > 0) state.MachineLnk = true;
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
        foreach (var f in state.Created.ToList())
        {
            try { if (File.Exists(f)) { File.Delete(f); n++; } } catch { }
        }
        foreach (var c in state.Ini.ToList())
        {
            try { if (File.Exists(c.File)) { DesktopIni.Write(c.File, "IconResource", c.Old); n++; } } catch { }
        }
        state.Reg.Clear(); state.Lnk.Clear(); state.Ini.Clear(); state.Created.Clear(); state.Sources.Clear(); state.Applied = "";
        StateStore.Save(state);
        Native.NotifyIconsChanged();
        return n;
    }
}
