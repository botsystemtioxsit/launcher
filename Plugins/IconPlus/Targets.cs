namespace IconPlusApp;

enum TargetKind { Folder, Drive, Clsid, FileType, Shortcuts, KnownFolders, AllFileTypes, StartApps }

// Что можно перекрасить или заменить. Ключевые слова — для автоматического подбора значков из набора по именам файлов.
sealed record Target(string Id, string Group, string Title, TargetKind Kind, string[] Keywords, string[]? Extensions = null, string? Clsid = null, string? ValueName = null, string[]? Reject = null, bool NeedsAdmin = false)
{
    public const string GFolders = "Папки и диски", GSystem = "Рабочий стол и система", GFiles = "Типы файлов", GShortcuts = "Ярлыки";

    public static readonly Target[] All =
    {
        new("folder", GFolders, "Папка (спросит разрешение администратора)", TargetKind.Folder, new[] { "folder", "directory", "папка", "dir", "folderclosed", "closed" }, Reject: new[] { "open", "открыт", "network", "music", "video", "picture", "download", "document", "desktop" }, NeedsAdmin: true),
        new("drive-fixed", GFolders, "Жёсткий диск", TargetKind.Drive, new[] { "harddrive", "hard drive", "hdd", "ssd", "disk", "drive", "диск", "локальный" }, Reject: new[] { "usb", "flash", "removable", "dvd", "cd", "optical", "network", "сеть" }),
        new("drive-removable", GFolders, "Флешка (USB)", TargetKind.Drive, new[] { "usb", "flash", "removable", "флешка", "съемный" }),
        new("drive-optical", GFolders, "DVD / CD", TargetKind.Drive, new[] { "dvd", "cd", "optical", "cdrom", "диск dvd", "bluray", "blu" }),
        new("drive-network", GFolders, "Сетевой диск", TargetKind.Drive, new[] { "network drive", "netdrive", "сетевой диск", "mapped" }),

        new("kf", GFolders, "Папки пользователя: Документы, Загрузки, Рабочий стол…", TargetKind.KnownFolders, Array.Empty<string>()),

        new("thispc", GSystem, "Этот компьютер", TargetKind.Clsid, new[] { "computer", "this pc", "mycomputer", "my computer", "pc", "компьютер", "desktop pc" }, Clsid: "{20D04FE0-3AEA-1069-A2D8-08002B30309D}", Reject: new[] { "network", "сеть", "control", "панель" }),
        new("recycle-empty", GSystem, "Корзина (пустая)", TargetKind.Clsid, new[] { "recycle", "trash", "bin", "корзина", "empty", "пуст" }, Clsid: "{645FF040-5081-101B-9F08-00AA002F954E}", ValueName: "empty", Reject: new[] { "full", "полн" }),
        new("recycle-full", GSystem, "Корзина (полная)", TargetKind.Clsid, new[] { "recycle full", "trash full", "bin full", "корзина полная", "full", "полн" }, Clsid: "{645FF040-5081-101B-9F08-00AA002F954E}", ValueName: "full"),
        new("network", GSystem, "Сеть", TargetKind.Clsid, new[] { "network", "сеть", "net", "lan" }, Clsid: "{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}", Reject: new[] { "drive", "диск" }),
        new("userfiles", GSystem, "Папка пользователя", TargetKind.Clsid, new[] { "user", "profile", "users", "пользователь", "home" }, Clsid: "{59031a47-3f72-44a7-89c5-5595fe6b30ee}"),

        new("ft-text", GFiles, "Текст (txt, log, ini, md)", TargetKind.FileType, new[] { "text", "txt", "log", "ini", "readme", "notepad", "текст" }, new[] { "txt", "log", "ini", "cfg", "md" }),
        new("ft-doc", GFiles, "Документ (doc, docx, rtf)", TargetKind.FileType, new[] { "doc", "docx", "word", "document", "rtf", "документ" }, new[] { "doc", "docx", "rtf", "odt" }),
        new("ft-sheet", GFiles, "Таблица (xls, xlsx, csv)", TargetKind.FileType, new[] { "xls", "xlsx", "excel", "sheet", "spreadsheet", "csv", "таблица" }, new[] { "xls", "xlsx", "csv", "ods" }),
        new("ft-slides", GFiles, "Презентация (ppt, pptx)", TargetKind.FileType, new[] { "ppt", "pptx", "powerpoint", "presentation", "slides", "презентация" }, new[] { "ppt", "pptx", "odp" }),
        new("ft-pdf", GFiles, "PDF", TargetKind.FileType, new[] { "pdf", "acrobat" }, new[] { "pdf" }),
        new("ft-archive", GFiles, "Архив (zip, rar, 7z)", TargetKind.FileType, new[] { "zip", "rar", "7z", "archive", "compressed", "tar", "gz", "архив" }, new[] { "zip", "rar", "7z", "tar", "gz" }),
        new("ft-image", GFiles, "Картинка (png, jpg, gif)", TargetKind.FileType, new[] { "image", "picture", "photo", "png", "jpg", "jpeg", "gif", "bmp", "картинка", "изображение", "фото" }, new[] { "png", "jpg", "jpeg", "gif", "bmp", "webp" }, Reject: new[] { "folder", "папка" }),
        new("ft-audio", GFiles, "Музыка (mp3, wav)", TargetKind.FileType, new[] { "audio", "music", "mp3", "wav", "flac", "sound", "музыка", "аудио" }, new[] { "mp3", "wav", "flac", "ogg", "m4a" }, Reject: new[] { "folder", "папка" }),
        new("ft-video", GFiles, "Видео (mp4, mkv, avi)", TargetKind.FileType, new[] { "video", "movie", "film", "mp4", "mkv", "avi", "видео", "фильм" }, new[] { "mp4", "mkv", "avi", "mov", "wmv" }, Reject: new[] { "folder", "папка" }),
        new("ft-web", GFiles, "Страница (html)", TargetKind.FileType, new[] { "html", "htm", "web", "page", "browser", "страница" }, new[] { "html", "htm" }),
        new("ft-code", GFiles, "Код (js, json, xml, py)", TargetKind.FileType, new[] { "code", "script", "source", "js", "json", "xml", "py", "python", "код" }, new[] { "js", "json", "xml", "py", "cs", "cpp", "h", "java" }),
        new("ft-script", GFiles, "Сценарий (bat, cmd, ps1)", TargetKind.FileType, new[] { "bat", "cmd", "ps1", "batch", "powershell", "shell" }, new[] { "bat", "cmd", "ps1" }),

        new("ft-all", GFiles, "Все остальные типы файлов (по списку Windows)", TargetKind.AllFileTypes, Array.Empty<string>()),

        new("lnk-desktop", GShortcuts, "Ярлыки на рабочем столе (и ярлыки-ссылки Steam)", TargetKind.Shortcuts, Array.Empty<string>()),
        new("lnk-desktop-all", GShortcuts, "Ярлыки на общем рабочем столе (для всех; спросит разрешение администратора)", TargetKind.Shortcuts, Array.Empty<string>(), NeedsAdmin: true),
        new("lnk-start", GShortcuts, "Ярлыки в меню «Пуск» (мои программы)", TargetKind.Shortcuts, Array.Empty<string>()),
        new("lnk-start-all", GShortcuts, "Ярлыки в меню «Пуск» (общие; спросит разрешение администратора)", TargetKind.Shortcuts, Array.Empty<string>(), NeedsAdmin: true),
        new("lnk-taskbar", GShortcuts, "Закреплённые на панели задач", TargetKind.Shortcuts, Array.Empty<string>()),
        new("start-apps", GShortcuts, "Приложения «Пуска» без ярлыков (магазинные и системные: Блокнот, Терминал, Безопасность Windows…)", TargetKind.StartApps, Array.Empty<string>()),
    };

    public static Target? ById(string id) => All.FirstOrDefault(t => t.Id == id);
}
