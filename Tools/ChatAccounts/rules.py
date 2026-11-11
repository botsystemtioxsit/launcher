"""Правила и проверка «скриптов» — чистая логика без сети и окон (её проверяют тесты).

Скрипт — это НАБОР НАСТРОЕК из закрытого списка действий, а не код. Чего программа не делает
(зашито здесь и в engine.py): не собирает списки участников, не пишет в личку людям, не вступает
в чужие чаты и не добавляет людей, не выполняет произвольный код.
"""
import re
import random
from datetime import datetime, timedelta, timezone

MIN_EVERY_MIN = 10          # не чаще раза в 10 минут в один чат
MAX_PER_DAY = 48            # лимит сообщений в сутки на чат (рассылка)
MAX_VARIANTS = 20
MAX_TEXT = 2000
MAX_REPLIES = 30
REPLY_COOLDOWN_S = 60       # ответ по ключевому слову — не чаще раза в минуту в чате
REPLY_MAX_PER_DAY = 30
WELCOME_GAP_S = 20          # приветствия — не чаще одного в 20 секунд на чат
WELCOME_MAX_PER_DAY = 100
CHAT_ID_RE = re.compile(r'^-?\d{5,20}$')
NAME_RE = re.compile(r'^[\w .\-]{1,40}$', re.UNICODE)
_CTRL = re.compile(r'[\x00-\x08\x0b\x0c\x0e-\x1f​-‏‪-‮⁦-⁩﻿]')


class ScriptError(ValueError):
    pass


def clean_text(s, limit=MAX_TEXT):
    s = _CTRL.sub('', str(s or '')).replace('\r\n', '\n').strip()
    return s[:limit]


def _int(v, lo, hi, name):
    try:
        n = int(v)
    except (TypeError, ValueError):
        raise ScriptError(f'«{name}» должно быть целым числом')
    if n < lo or n > hi:
        raise ScriptError(f'«{name}» — от {lo} до {hi}')
    return n


def default_script():
    return {'name': '', 'account': '', 'chats': [], 'enabled': True,
            'post': {'enabled': False, 'variants': [], 'order': 'random', 'everyMin': 60, 'perDay': 12,
                     'from': 9, 'to': 23, 'tz': 3},
            'welcome': {'enabled': False, 'text': ''},
            'replies': []}


def validate(raw):
    """Возвращает очищенную копию или бросает ScriptError. Лишние поля выбрасываются."""
    if not isinstance(raw, dict):
        raise ScriptError('скрипт должен быть набором настроек')
    s = default_script()
    name = clean_text(raw.get('name'), 40)
    if not NAME_RE.match(name):
        raise ScriptError('название — 1–40 знаков: буквы, цифры, пробел, точка, дефис')
    s['name'] = name
    acc = clean_text(raw.get('account'), 40)
    if not NAME_RE.match(acc):
        raise ScriptError('выберите аккаунт')
    s['account'] = acc
    chats = raw.get('chats')
    if not isinstance(chats, list) or not chats:
        raise ScriptError('выберите хотя бы один чат')
    if len(chats) > 30:
        raise ScriptError('не больше 30 чатов в одном скрипте')
    out = []
    for c in chats:
        c = str(c)
        if not CHAT_ID_RE.match(c):
            raise ScriptError('неверный номер чата')
        if c not in out:
            out.append(c)
    s['chats'] = out
    s['enabled'] = bool(raw.get('enabled', True))

    p = raw.get('post') if isinstance(raw.get('post'), dict) else {}
    if p.get('enabled'):
        v = [clean_text(x) for x in (p.get('variants') or [])]
        v = [x for x in v if x]
        if not v:
            raise ScriptError('в «Рассылке» нужен хотя бы один текст')
        if len(v) > MAX_VARIANTS:
            raise ScriptError(f'не больше {MAX_VARIANTS} вариантов текста')
        s['post'] = {'enabled': True, 'variants': v, 'order': 'cycle' if p.get('order') == 'cycle' else 'random',
                     'everyMin': _int(p.get('everyMin', 60), MIN_EVERY_MIN, 10080, 'интервал, минут'),
                     'perDay': _int(p.get('perDay', 12), 1, MAX_PER_DAY, 'сообщений в сутки'),
                     'from': _int(p.get('from', 9), 0, 23, 'начало часов'),
                     'to': _int(p.get('to', 23), 0, 24, 'конец часов'),
                     'tz': _int(p.get('tz', 3), -12, 14, 'часовой пояс')}
    w = raw.get('welcome') if isinstance(raw.get('welcome'), dict) else {}
    if w.get('enabled'):
        t = clean_text(w.get('text'))
        if not t:
            raise ScriptError('введите текст приветствия')
        s['welcome'] = {'enabled': True, 'text': t}
    reps = raw.get('replies') or []
    if not isinstance(reps, list):
        raise ScriptError('ответы по ключевым словам — список')
    if len(reps) > MAX_REPLIES:
        raise ScriptError(f'не больше {MAX_REPLIES} правил ответов')
    for r in reps:
        if not isinstance(r, dict):
            raise ScriptError('неверное правило ответа')
        kws = [clean_text(k, 40).lower() for k in (r.get('keywords') or [])]
        kws = [k for k in kws if len(k) >= 2]
        txt = clean_text(r.get('text'))
        if not kws or not txt:
            raise ScriptError('в правиле ответа нужны ключевые слова (от 2 знаков) и текст')
        s['replies'].append({'keywords': kws[:10], 'text': txt})
    if not (s['post']['enabled'] or s['welcome']['enabled'] or s['replies']):
        raise ScriptError('включите хотя бы одно действие: рассылку, приветствие или ответы')
    return s


# ---------- расписание ----------
def local_now(now, tz):
    return now.astimezone(timezone(timedelta(hours=tz)))


def in_hours(now, cfg):
    h = local_now(now, cfg['tz']).hour
    a, b = cfg['from'], cfg['to']
    if a == b:
        return True
    return a <= h < b if a < b else (h >= a or h < b)


def day_key(now, tz):
    return local_now(now, tz).strftime('%Y-%m-%d')


def post_due(state, cfg, now):
    """state: {'at': epoch сек, 'day': '...', 'n': число за день}. True — пора отправлять."""
    if not in_hours(now, cfg):
        return False
    n = state.get('n', 0) if state.get('day') == day_key(now, cfg['tz']) else 0
    if n >= cfg['perDay']:
        return False
    return now.timestamp() - float(state.get('at', 0)) >= cfg['everyMin'] * 60


def after_post(state, cfg, now):
    day = day_key(now, cfg['tz'])
    n = (state.get('n', 0) if state.get('day') == day else 0) + 1
    return {**state, 'at': now.timestamp(), 'day': day, 'n': n}


def pick_variant(variants, order, state):
    if order == 'cycle':
        i = int(state.get('i', 0)) % len(variants)
        return variants[i], {**state, 'i': i + 1}
    if len(variants) == 1:
        return variants[0], state
    last = state.get('last')
    choices = [i for i in range(len(variants)) if i != last]
    i = random.choice(choices)
    return variants[i], {**state, 'last': i}


def fill(text, name='', chat=''):
    return text.replace('{name}', clean_text(name, 60) or 'друг').replace('{chat}', clean_text(chat, 80))


def reply_for(replies, message):
    m = (message or '').lower()
    for r in replies:
        if any(k in m for k in r['keywords']):
            return r['text']
    return None


def gate(state, now, gap_s, max_day, tz=0):
    """Общий предохранитель для коротких действий (приветствие/ответ). Возвращает (можно, новое_state)."""
    day = day_key(now, tz)
    n = state.get('n', 0) if state.get('day') == day else 0
    if n >= max_day or now.timestamp() - float(state.get('at', 0)) < gap_s:
        return False, state
    return True, {**state, 'at': now.timestamp(), 'day': day, 'n': n + 1}


def jitter(seconds):
    """Случайная добавка к паузе, чтобы действия не шли по линейке."""
    return seconds + random.uniform(0, seconds * 0.3)
