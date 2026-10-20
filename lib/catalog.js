// Что умеет ставить лаунчер. Только эти два приложения и только из релизов
// этих репозиториев: имя файла и адрес скачивания проверяются по этому списку.
const OWNER = 'botsystemtioxsit';

const APPS = [
  {
    id: 'troll-battle',
    name: 'Тролль-Баттл',
    desc: 'Голосовые баттлы в десктопном окне — та же игра, что в Telegram.',
    repo: 'index.html',
    productName: 'Тролль-Баттл',
    roles: null, // доступно любому аккаунту
  },
  {
    id: 'battle-admin',
    name: 'БАТТЛ Админ',
    desc: 'Панель администратора: игроки, заявки, журнал, бэкапы.',
    repo: 'battle-admin-bot',
    productName: 'БАТТЛ Админ',
    roles: ['moderator', 'admin', 'superadmin'],
  },
];

const byId = (id) => APPS.find((a) => a.id === id) || null;

function isAllowed(app, role) {
  return !app.roles || app.roles.includes(role);
}

module.exports = { OWNER, APPS, byId, isAllowed };
