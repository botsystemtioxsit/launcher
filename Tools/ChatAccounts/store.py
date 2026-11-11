"""Хранение: настройки и входы аккаунтов в %APPDATA%\\GhostShellChatAccounts.
Вход в аккаунт (строка сессии) шифруется средствами Windows (DPAPI, только для вашей учётной записи Windows)."""
import base64
import ctypes
import json
import os
import sys
import re
from ctypes import wintypes

DIR = os.path.join(os.environ.get('APPDATA') or os.path.expanduser('~'), 'GhostShellChatAccounts')
ACC_RE = re.compile(r'^[\w .\-]{1,40}$', re.UNICODE)
_ENTROPY = b'GhostShell.ChatAccounts.v1'


class _Blob(ctypes.Structure):
    _fields_ = [('cbData', wintypes.DWORD), ('pbData', ctypes.POINTER(ctypes.c_char))]


def _blob(b):
    return _Blob(len(b), ctypes.cast(ctypes.create_string_buffer(b, len(b)), ctypes.POINTER(ctypes.c_char)))


def protect(data: bytes) -> bytes:
    if sys.platform != 'win32':
        if os.environ.get('GS_ALLOW_PLAIN') == '1':   # только для проверок на не-Windows
            return b'PLAIN:' + data
        raise RuntimeError('шифрование входа доступно только в Windows')
    out = _Blob()
    if not ctypes.windll.crypt32.CryptProtectData(ctypes.byref(_blob(data)), None, ctypes.byref(_blob(_ENTROPY)), None, None, 0, ctypes.byref(out)):
        raise RuntimeError('Windows не смогла зашифровать вход')
    try:
        return ctypes.string_at(out.pbData, out.cbData)
    finally:
        ctypes.windll.kernel32.LocalFree(out.pbData)


def unprotect(data: bytes) -> bytes:
    if sys.platform != 'win32':
        if os.environ.get('GS_ALLOW_PLAIN') == '1' and data.startswith(b'PLAIN:'):
            return data[6:]
        raise RuntimeError('шифрование входа доступно только в Windows')
    out = _Blob()
    if not ctypes.windll.crypt32.CryptUnprotectData(ctypes.byref(_blob(data)), None, ctypes.byref(_blob(_ENTROPY)), None, None, 0, ctypes.byref(out)):
        raise RuntimeError('не удалось расшифровать вход (другой пользователь Windows?)')
    try:
        return ctypes.string_at(out.pbData, out.cbData)
    finally:
        ctypes.windll.kernel32.LocalFree(out.pbData)


def _path(*p):
    os.makedirs(DIR, exist_ok=True)
    return os.path.join(DIR, *p)


def _atomic_write(path, data: bytes):
    tmp = path + '.tmp'
    with open(tmp, 'wb') as f:
        f.write(data)
    os.replace(tmp, path)


def save_json(name, obj):
    _atomic_write(_path(name), json.dumps(obj, ensure_ascii=False, indent=1).encode('utf-8'))


def load_json(name, default):
    try:
        with open(_path(name), 'rb') as f:
            return json.loads(f.read().decode('utf-8'))
    except (OSError, ValueError):
        return default


# аккаунты: accounts.json = {имя: {apiId, phone}}, сам вход и apiHash — accounts\<имя>.dat (зашифровано)
def list_accounts():
    d = load_json('accounts.json', {})
    return d if isinstance(d, dict) else {}


def save_account(name, api_id, api_hash, phone, session):
    if not ACC_RE.match(name):
        raise ValueError('неверное имя аккаунта')
    os.makedirs(os.path.join(DIR, 'accounts'), exist_ok=True)
    secret = json.dumps({'apiHash': api_hash, 'session': session}).encode('utf-8')
    _atomic_write(_path('accounts', name + '.dat'), protect(secret))
    d = list_accounts()
    d[name] = {'apiId': int(api_id), 'phone': phone}
    save_json('accounts.json', d)


def load_secret(name):
    if not ACC_RE.match(name):
        raise ValueError('неверное имя аккаунта')
    with open(_path('accounts', name + '.dat'), 'rb') as f:
        return json.loads(unprotect(f.read()).decode('utf-8'))


def forget_account(name):
    if not ACC_RE.match(name):
        return
    try:
        os.remove(_path('accounts', name + '.dat'))
    except OSError:
        pass
    d = list_accounts()
    d.pop(name, None)
    save_json('accounts.json', d)
