// Минимальный клиент Firebase Realtime Database поверх REST — без SDK и без
// Firebase Auth (проект на бесплатном плане Spark, Auth не используем нигде).
// База открыта на чтение/запись (database.rules.json в репозитории index.html),
// поэтому всё, что отсюда читается, — это подсказка для интерфейса, а не
// доказательство личности: настоящий замок — подтверждение входа человеком.
const DB_URL = 'https://zolotaya-kletka-default-rtdb.firebaseio.com';
const GATEWAY_URL = 'https://battle-role-gateway.tioxsit.workers.dev';
const TIMEOUT_MS = 12000;

// Любой путь, собранный из чужого текста, сначала проходит через это.
const SAFE_ID_RE = /^[A-Za-z0-9_-]{1,64}$/;

function emailToKey(email) {
  // та же схема, что в игре и админ-панели: «.» запрещена в ключах RTDB
  return String(email).trim().toLowerCase().replace(/\./g, ',');
}

function isValidEmail(email) {
  const s = String(email).trim();
  return s.length <= 254 && /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(s);
}

async function call(method, path, body) {
  const res = await fetch(`${DB_URL}/${path}.json`, {
    method,
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
    signal: AbortSignal.timeout(TIMEOUT_MS),
  });
  if (!res.ok) throw new Error(`База данных ответила ${res.status}`);
  return res.json();
}

const get = (path) => call('GET', path);
const post = (path, body) => call('POST', path, body);
const del = (path) => call('DELETE', path);

// Читаем поля по одному: в users/<id> лежит аватарка в base64, целиком не нужна.
async function getProfile(id) {
  if (!SAFE_ID_RE.test(id)) throw new Error('Некорректный id');
  const [firstName, role, status, playerId] = await Promise.all(
    ['firstName', 'role', 'status', 'playerId'].map((f) => get(`users/${id}/${f}`)),
  );
  return {
    id,
    name: String(firstName || '').slice(0, 40),
    role: String(role || 'user').slice(0, 20),
    status: String(status || 'active').slice(0, 20),
    playerId: String(playerId || '').slice(0, 24),
  };
}

async function findAccountByEmail(email) {
  const id = await get(`emailLogins/${encodeURIComponent(emailToKey(email))}`);
  return id && SAFE_ID_RE.test(String(id)) ? String(id) : null;
}

async function createLoginRequest(targetId) {
  if (!SAFE_ID_RE.test(targetId)) throw new Error('Некорректный id');
  // Метка устройства попадает в сообщение бота владельцу — Worker принимает
  // только буквы/цифры/пробел и . _ ( ) , / -
  const out = await post(`users/${targetId}/loginRequests`, {
    device: 'Лаунчер (Windows)',
    app: 'launcher',
    requestedAt: Date.now(),
    status: 'pending',
  });
  const requestId = out && out.name;
  if (!requestId || !SAFE_ID_RE.test(requestId)) throw new Error('База не вернула номер заявки');
  // Бот пишет владельцу в Telegram с кнопками «Разрешить/Отклонить».
  // Не вышло — заявку всё равно можно подтвердить в игре или в админ-панели.
  fetch(`${GATEWAY_URL}/login-notify`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ telegramId: targetId, requestId }),
    signal: AbortSignal.timeout(TIMEOUT_MS),
  }).catch(() => {});
  return requestId;
}

async function getLoginRequestStatus(targetId, requestId) {
  if (!SAFE_ID_RE.test(targetId) || !SAFE_ID_RE.test(requestId)) throw new Error('Некорректный id');
  const v = await get(`users/${targetId}/loginRequests/${requestId}`);
  return v && typeof v.status === 'string' ? v.status : null;
}

async function removeLoginRequest(targetId, requestId) {
  if (!SAFE_ID_RE.test(targetId) || !SAFE_ID_RE.test(requestId)) return;
  await del(`users/${targetId}/loginRequests/${requestId}`).catch(() => {});
}

module.exports = {
  SAFE_ID_RE, emailToKey, isValidEmail,
  getProfile, findAccountByEmail,
  createLoginRequest, getLoginRequestStatus, removeLoginRequest,
};
