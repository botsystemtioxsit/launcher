// Маленькое JSON-хранилище в папке данных приложения. Ничего секретного:
// список аккаунтов (id, имя, роль) и время последней проверки обновлений.
const fs = require('fs');
const path = require('path');

function createStore(dir, name, defaults) {
  const file = path.join(dir, name);
  let data = { ...defaults };
  try {
    data = { ...defaults, ...JSON.parse(fs.readFileSync(file, 'utf8')) };
  } catch (_) { /* первого запуска файла ещё нет — это нормально */ }
  return {
    get: () => data,
    set(patch) {
      data = { ...data, ...patch };
      fs.mkdirSync(dir, { recursive: true });
      const tmp = file + '.tmp';
      fs.writeFileSync(tmp, JSON.stringify(data, null, 2));
      fs.renameSync(tmp, file); // запись целиком или никак — файл не останется обрезанным
    },
  };
}

module.exports = { createStore };
