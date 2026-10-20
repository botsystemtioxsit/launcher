// Установка, запуск и удаление приложений. Установщики приложений — обычные
// NSIS-установщики electron-builder (на пользователя, без прав администратора):
// запускаем их с /S (тихо) и потом читаем версию из реестра Windows.
const fs = require('fs');
const os = require('os');
const path = require('path');
const crypto = require('crypto');
const { spawn, execFile } = require('child_process');
const { Readable } = require('stream');
const { pipeline } = require('stream/promises');
const { isTrustedDownloadUrl } = require('./releases');

const isWindows = process.platform === 'win32';
const UNINSTALL_KEYS = [
  'HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall',
  'HKLM\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall',
];

function run(file, args) {
  return new Promise((resolve) => {
    execFile(file, args, { windowsHide: true, maxBuffer: 16 * 1024 * 1024, encoding: 'utf8' },
      (err, stdout) => resolve(err ? '' : stdout));
  });
}

// Разбор вывода `reg query <ключ> /s` на блоки по подключам.
function parseRegDump(text) {
  const entries = [];
  let cur = null;
  for (const raw of text.split(/\r?\n/)) {
    if (/^HK/.test(raw)) { cur = {}; entries.push(cur); continue; }
    const m = /^\s{4}(\S+)\s+REG_\w+\s+(.*)$/.exec(raw);
    if (m && cur) cur[m[1]] = m[2];
  }
  return entries;
}

// Находит установленное приложение по названию в списке «Установка и удаление
// программ». DisplayName у electron-builder — название приложения (иногда с версией).
async function findInstalled(productName) {
  if (!isWindows) return null;
  for (const key of UNINSTALL_KEYS) {
    const entries = parseRegDump(await run('reg', ['query', key, '/s']));
    const hit = entries.find((e) => e.DisplayName && (e.DisplayName === productName || e.DisplayName.startsWith(productName + ' ')));
    if (!hit) continue;
    const location = (hit.InstallLocation || '').replace(/^"|"$/g, '') ||
      path.dirname((hit.UninstallString || '').replace(/^"|".*$/g, ''));
    const exe = path.join(location, `${productName}.exe`);
    if (!fs.existsSync(exe)) continue;
    return { version: hit.DisplayVersion || null, exe, uninstall: hit.QuietUninstallString || hit.UninstallString || null, machineWide: key.startsWith('HKLM') };
  }
  return null;
}

async function download(url, repo, expectedSha512, onProgress) {
  if (!isTrustedDownloadUrl(url, repo)) throw new Error('Недоверенный адрес загрузки');
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'battle-launcher-'));
  const file = path.join(dir, 'setup.exe');
  try {
    const res = await fetch(url, { headers: { 'User-Agent': 'battle-launcher' }, signal: AbortSignal.timeout(10 * 60 * 1000) });
    if (!res.ok || !res.body) throw new Error(`Не скачалось: HTTP ${res.status}`);
    const total = Number(res.headers.get('content-length')) || 0;
    let got = 0;
    const hash = crypto.createHash('sha512');
    const body = Readable.fromWeb(res.body);
    body.on('data', (chunk) => {
      got += chunk.length;
      hash.update(chunk);
      if (total) onProgress(Math.min(99, Math.floor((got / total) * 100)));
    });
    await pipeline(body, fs.createWriteStream(file));
    // sha512 из latest.yml (base64) — защита от битого или подменённого файла
    if (hash.digest('base64') !== expectedSha512) throw new Error('Контрольная сумма не совпала — файл повреждён');
    return { file, dir };
  } catch (err) {
    fs.rmSync(dir, { recursive: true, force: true });
    throw err;
  }
}

function runAndWait(file, args) {
  return new Promise((resolve, reject) => {
    const child = spawn(file, args, { windowsHide: true, stdio: 'ignore' });
    child.on('error', reject);
    child.on('exit', (code) => (code === 0 ? resolve() : reject(new Error(`Установщик завершился с кодом ${code}`))));
  });
}

async function install(app, release, onProgress) {
  if (!isWindows) throw new Error('Установка работает только в Windows');
  const { file, dir } = await download(release.url, app.repo, release.sha512, onProgress);
  try {
    onProgress(null); // скачано — дальше «Устанавливаю…» без процентов
    await runAndWait(file, ['/S']);
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
  const found = await findInstalled(app.productName);
  if (!found) throw new Error('Установщик отработал, но приложение не найдено');
  return found;
}

function launch(exe) {
  const child = spawn(exe, [], { detached: true, stdio: 'ignore', cwd: path.dirname(exe) });
  child.on('error', () => {});
  child.unref();
}

async function uninstall(found) {
  if (!isWindows || !found.uninstall) throw new Error('Не нашёл программу удаления');
  // строка вида: "C:\...\Uninstall X.exe" /currentuser — добавляем /S для тихого режима
  const m = /^"([^"]+)"\s*(.*)$/.exec(found.uninstall.trim());
  const exe = m ? m[1] : found.uninstall.trim();
  const rest = m && m[2] ? m[2].split(/\s+/).filter(Boolean) : [];
  if (!/\.exe$/i.test(exe) || !fs.existsSync(exe)) throw new Error('Программа удаления не найдена');
  await runAndWait(exe, [...rest, '/S']);
}

module.exports = { isWindows, findInstalled, install, launch, uninstall, parseRegDump };
