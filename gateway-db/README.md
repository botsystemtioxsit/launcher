# Отдельная база Gateway Plus (проект Firebase `vpn0dns`)

Правила `database.rules.json` — для базы `vpn0dns-default-rtdb` (регион europe-west1). Идея: **в этой базе клиенту запрещено всё, кроме двух вещей** —
создать/обновить свою запись `gatewayAccounts/<номер>` (удалять нельзя, `createdAt` не меняется) и читать записи по точному ключу.
Блокировки (`gatewayBlocks`, `gatewayBlockedDevices`) клиент записать и **стереть** не может — их пишет только Worker (`/gateway-block`) токеном сервисного аккаунта.

Выкладка: workflow **Deploy Gateway DB rules** (секрет репозитория `VPN_FIREBASE_SERVICE_ACCOUNT` — JSON-ключ сервисного аккаунта проекта `vpn0dns`).
Тот же ключ нужен Worker'у (секрет `GATEWAY_FIREBASE_SERVICE_ACCOUNT`, см. CLAUDE.md репозитория `battle-admin-bot`).
