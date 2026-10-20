# CLAUDE.md

## What this is
«БАТТЛ Лаунчер» — **нативное** Windows-приложение (C# / .NET 8 / WPF, один самодостаточный
`BattleLauncher.exe`). Не Electron, не веб-обёртка — владелец это прямо запретил; не
возвращать веб-технологии в сам лаунчер. Аккаунты + установка/обновление/запуск/удаление
«Тролль-Баттл» (репо `index.html`) и «БАТТЛ Админ» (репо `battle-admin-bot`). Тот же Firebase
(`zolotaya-kletka`, Spark, **без биллинга, никогда**), только REST, без SDK и без Firebase Auth.

## Layout (`Launcher/`)
- `MainWindow.xaml(.cs)` — весь интерфейс: вход, аккаунты, список приложений. Данные из базы и
  GitHub выводятся только как `Text`.
- `Services/Rtdb.cs` — REST к RTDB и Worker `/login-notify`; `Releases.cs` — версии из GitHub
  Releases (`latest.yml`); `Installer.cs` — реестр, скачивание + sha512, `/S`, запуск, удаление;
  `SelfUpdate.cs` — обновление самого лаунчера; `Catalog.cs` — список приложений и ролей;
  `AccountStore.cs` — `%APPDATA%\BattleLauncher\launcher.json`.

## Rules
- Вход = заявка `users/<id>/loginRequests` как в игре/админке (`app: 'launcher'`; Worker для
  неизвестного `app` пишет общий текст). Секретов не хранится: только id/имя/роль. Роли в UI —
  удобство, не защита (база открыта на запись).
- Новое приложение = запись в `Catalog.cs`; в его репо нужен релиз с `latest.yml`
  (`electron-builder --publish always`). Если приложение станет нативным — научить `Installer`/`Releases` его формату.
- `FindInstalled` ищет `DisplayName == ProductName` в реестре и `<ProductName>.exe` в папке установки.
- Версию лаунчера поднимать в `Launcher.csproj` (`<Version>`) при каждом изменении, иначе релиз не обновится.
- Из этой среды .NET не поставить и Windows нет: компилирует и собирает GitHub Actions
  (смотреть логи job через GitHub MCP). Работу окна здесь проверить нельзя.

## Working agreement with the owner
Владелец не технарь, говорит по-русски. Пишем коротко и просто; своё — сначала вопросом «да/нет»; ограничения называем прямо.
