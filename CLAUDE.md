# CLAUDE.md

## What this is
«БАТТЛ Лаунчер» — Electron-приложение для Windows (NSIS `.exe`): аккаунты +
установка/обновление/запуск/удаление «Тролль-Баттл» (репо `index.html`) и
«БАТТЛ Админ» (репо `battle-admin-bot`). Тот же Firebase-проект
(`zolotaya-kletka`, Spark, **без биллинга, никогда**). Серверной части нет и не
добавлять; Firebase SDK и Firebase Auth не использовать — только REST.

## Layout
- `main.js` — окно, IPC, аккаунты, вход, самообновление (`electron-updater`).
- `preload.js` — единственный мост (`window.launcher`), `contextIsolation`, `sandbox`.
- `lib/rtdb.js` — REST к RTDB и Worker `/login-notify`; `lib/releases.js` — версии из
  GitHub Releases (`latest.yml`) + проверка адреса загрузки; `lib/installer.js` —
  реестр Windows, скачивание+sha512, `/S`-установка, запуск, удаление;
  `lib/catalog.js` — список приложений и ролей.
- `renderer/` — интерфейс; всё из базы/GitHub только через `textContent`, CSP без inline.
- `scripts/check.js` — `npm run check` (синтаксис + чистые функции), гоняется в CI.

## Rules
- Вход = заявка `users/<id>/loginRequests` как в игре/админке (`app: 'launcher'`; Worker
  для неизвестного `app` пишет общий текст). Ничего секретного не хранится: в
  `launcher.json` только id/имя/роль. Права в UI — удобство, не защита (база открыта).
- Новое приложение = запись в `lib/catalog.js`; в репо должен быть релиз с `latest.yml`
  (его делает `electron-builder --publish always`).
- Менять путь/имя установщика приложений нельзя без проверки `findInstalled`: он ищет
  `DisplayName` = `productName` в реестре и `<productName>.exe` в папке установки.
- Версию лаунчера поднимать в `package.json` при каждом изменении, иначе релиз не обновится.
- Windows-сборку из этой среды проверить нельзя (нет Windows/wine): собирает Actions.

## Working agreement with the owner
Владелец не технарь, говорит по-русски. Пишем коротко и просто; своё — сначала
вопросом «да/нет»; ограничения называем прямо.
