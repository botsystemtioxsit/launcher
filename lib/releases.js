// Последняя версия приложения — из релизов GitHub (их публикует electron-builder
// вместе с latest.yml: версия, имя файла и sha512 установщика).
const { OWNER } = require('./catalog');

const TIMEOUT_MS = 15000;
const CACHE_MS = 5 * 60 * 1000; // без токена у GitHub лимит 60 запросов в час на IP
const cache = new Map();

function parseLatestYml(text) {
  const pick = (key) => {
    const m = new RegExp(`^${key}:\\s*(.+?)\\s*$`, 'm').exec(text);
    return m ? m[1].replace(/^['"]|['"]$/g, '') : null;
  };
  const version = pick('version');
  const file = pick('path');
  const sha512 = pick('sha512');
  if (!version || !file || !sha512 || !/^\d+\.\d+\.\d+/.test(version)) throw new Error('latest.yml повреждён');
  return { version, file, sha512 };
}

async function fetchText(url) {
  const res = await fetch(url, { signal: AbortSignal.timeout(TIMEOUT_MS), headers: { 'User-Agent': 'battle-launcher' } });
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  return res.text();
}

const base = (repo) => `https://github.com/${OWNER}/${repo}/releases`;

// Берём не просто «latest»: в репозитории могут быть релизы без установщика
// (например, APK), поэтому ищем самый свежий, где есть latest.yml.
async function viaApi(repo) {
  const res = await fetch(`https://api.github.com/repos/${OWNER}/${repo}/releases?per_page=10`, {
    signal: AbortSignal.timeout(TIMEOUT_MS),
    headers: { Accept: 'application/vnd.github+json', 'User-Agent': 'battle-launcher' },
  });
  if (!res.ok) throw new Error(`GitHub API ${res.status}`);
  const list = await res.json();
  for (const rel of list) {
    if (rel.draft || rel.prerelease) continue;
    const yml = (rel.assets || []).find((a) => a.name === 'latest.yml');
    if (!yml) continue;
    const info = parseLatestYml(await fetchText(yml.browser_download_url));
    // electron-builder заменяет пробелы и не-ASCII в имени файла на «-»/«.» на GitHub,
    // поэтому ищем по имени из yml, а запасным вариантом берём единственный .exe
    const exes = (rel.assets || []).filter((a) => /\.exe$/i.test(a.name));
    const asset = exes.find((a) => a.name === info.file) || (exes.length === 1 ? exes[0] : null);
    if (!asset) continue;
    return { ...info, url: asset.browser_download_url, size: asset.size || 0 };
  }
  throw new Error('В релизах нет установщика');
}

// Запасной путь без API (и без его лимита): ссылка «latest/download».
async function viaDirect(repo) {
  const info = parseLatestYml(await fetchText(`${base(repo)}/latest/download/latest.yml`));
  return { ...info, url: `${base(repo)}/latest/download/${encodeURIComponent(info.file)}`, size: 0 };
}

async function latestRelease(repo, { force = false } = {}) {
  const hit = cache.get(repo);
  if (!force && hit && Date.now() - hit.at < CACHE_MS) return hit.value;
  let value;
  try {
    value = await viaApi(repo);
  } catch (_) {
    value = await viaDirect(repo);
  }
  cache.set(repo, { at: Date.now(), value });
  return value;
}

// Скачивать разрешено только с github.com из релизов нужного репозитория.
function isTrustedDownloadUrl(url, repo) {
  try {
    const u = new URL(url);
    return u.protocol === 'https:' && u.hostname === 'github.com' &&
      u.pathname.startsWith(`/${OWNER}/${repo}/releases/`);
  } catch (_) {
    return false;
  }
}

// «1.2.10» > «1.2.9»; без предрелизных суффиксов — проект их не публикует.
function compareVersions(a, b) {
  const pa = String(a).split('.').map((n) => parseInt(n, 10) || 0);
  const pb = String(b).split('.').map((n) => parseInt(n, 10) || 0);
  for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
    const d = (pa[i] || 0) - (pb[i] || 0);
    if (d) return d > 0 ? 1 : -1;
  }
  return 0;
}

module.exports = { latestRelease, isTrustedDownloadUrl, compareVersions, parseLatestYml };
