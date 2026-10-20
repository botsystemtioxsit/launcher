// Проверка без Electron: разбор файлов и чистые функции. Запуск: npm run check
const fs = require('fs');
const path = require('path');
const assert = require('assert');
const root = path.join(__dirname, '..');
for (const f of ['main.js', 'preload.js', 'lib/rtdb.js', 'lib/store.js', 'lib/catalog.js', 'lib/releases.js', 'lib/installer.js', 'renderer/app.js']) {
  new Function(fs.readFileSync(path.join(root, f), 'utf8').replace(/^#!.*/, ''));
}
const { compareVersions, parseLatestYml, isTrustedDownloadUrl } = require('../lib/releases');
const { parseRegDump } = require('../lib/installer');
const { emailToKey, isValidEmail } = require('../lib/rtdb');
assert.strictEqual(compareVersions('1.2.10', '1.2.9'), 1);
assert.strictEqual(compareVersions('1.0.0', '1.0'), 0);
assert.strictEqual(compareVersions('1.0.6', '1.1.0'), -1);
assert.deepStrictEqual(parseLatestYml("version: 1.0.6\nfiles:\n  - url: x.exe\n    sha512: AAA\npath: x.exe\nsha512: BBB\n"), { version: '1.0.6', file: 'x.exe', sha512: 'BBB' });
assert.ok(isTrustedDownloadUrl('https://github.com/botsystemtioxsit/index.html/releases/download/v1/a.exe', 'index.html'));
assert.ok(!isTrustedDownloadUrl('https://evil.com/botsystemtioxsit/index.html/releases/a.exe', 'index.html'));
assert.ok(!isTrustedDownloadUrl('https://github.com/other/index.html/releases/a.exe', 'index.html'));
assert.strictEqual(emailToKey(' A.b@Ex.com '), 'a,b@ex,com');
assert.ok(isValidEmail('a@b.co') && !isValidEmail('abc'));
const dump = 'HKEY_CURRENT_USER\\X\\{1}\r\n    DisplayName    REG_SZ    Тролль-Баттл\r\n    DisplayVersion    REG_SZ    1.0.4\r\n\r\n';
assert.strictEqual(parseRegDump(dump)[0].DisplayVersion, '1.0.4');
console.log('OK');
