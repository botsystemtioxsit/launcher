"""Движок: подключает ваши аккаунты (Telethon / MTProto) и выполняет скрипты из rules.py.

Жёсткие границы (проверяются здесь, а не только в окне):
 • пишет только в чаты из скрипта и только в группы/каналы — любая цель-человек отклоняется;
 • нет сбора участников, нет личных сообщений, нет вступления в чаты и добавления людей;
 • интервалы, суточные лимиты, пауза между любыми двумя сообщениями аккаунта и случайные задержки;
 • FloodWait и запрет писать → пауза или отключение чата, а не упорство.
"""
import asyncio
import random
import threading
from datetime import datetime, timezone

from telethon import TelegramClient, events, errors
from telethon.sessions import StringSession
from telethon.tl.types import User

import rules
import store

ACCOUNT_GAP_S = 4          # минимум между двумя сообщениями одного аккаунта


class Engine:
    def __init__(self, log):
        self.log = log                      # log(текст) — вызывается из любого потока
        self.loop = asyncio.new_event_loop()
        self.thread = threading.Thread(target=self.loop.run_forever, daemon=True)
        self.thread.start()
        self.clients = {}                   # имя аккаунта -> TelegramClient
        self.locks = {}                     # имя аккаунта -> (Lock, время последней отправки)
        self.tasks = []
        self.pending = {}                   # вход в процессе: имя -> (client, phone)
        self.running = False
        self.state = store.load_json('state.json', {})
        self.dead = set()                   # (скрипт, чат), где писать нельзя

    # ---------- обвязка ----------
    def submit(self, coro):
        return asyncio.run_coroutine_threadsafe(coro, self.loop)

    def _save_state(self):
        try:
            store.save_json('state.json', self.state)
        except OSError as e:
            self.log(f'Не удалось сохранить состояние: {e}')

    # ---------- вход в аккаунт ----------
    async def login_send_code(self, name, api_id, api_hash, phone):
        c = TelegramClient(StringSession(), int(api_id), api_hash)
        await c.connect()
        await c.send_code_request(phone)
        self.pending[name] = (c, phone, int(api_id), api_hash)

    async def login_confirm(self, name, code, password=None):
        c, phone, api_id, api_hash = self.pending[name]
        try:
            if password and not code:
                await c.sign_in(password=password)
            else:
                await c.sign_in(phone, code)
        except errors.SessionPasswordNeededError:
            if not password:
                return 'password'
            await c.sign_in(password=password)
        store.save_account(name, api_id, api_hash, phone, c.session.save())
        me = await c.get_me()
        await c.disconnect()
        self.pending.pop(name, None)
        return 'ok:' + ((me.first_name or '') + ' ' + (me.last_name or '')).strip()

    # ---------- чаты аккаунта (для выбора в окне) ----------
    async def list_chats(self, name):
        c = await self._client(name)
        res = []
        async for d in c.iter_dialogs(limit=300):
            if d.is_group or d.is_channel:
                res.append((str(d.id), (d.title or str(d.id))[:80]))
        return res

    async def _client(self, name):
        c = self.clients.get(name)
        if c and c.is_connected():
            return c
        sec = store.load_secret(name)
        acc = store.list_accounts().get(name)
        if not acc:
            raise RuntimeError('аккаунт не найден')
        c = TelegramClient(StringSession(sec['session']), int(acc['apiId']), sec['apiHash'])
        await c.connect()
        if not await c.is_user_authorized():
            raise RuntimeError(f'аккаунт «{name}» разлогинен — добавьте его заново')
        await c.get_dialogs(limit=300)      # прогрев кэша, чтобы находить чаты по номеру
        self.clients[name] = c
        self.locks[name] = [asyncio.Lock(), 0.0]
        return c

    # ---------- запуск / остановка ----------
    async def start(self, scripts):
        if self.running:
            return
        self.running = True
        self.dead.clear()
        by_acc = {}
        for s in scripts:
            if s.get('enabled', True):
                by_acc.setdefault(s['account'], []).append(s)
        if not by_acc:
            self.running = False
            self.log('Нет включённых скриптов.')
            return
        for name, lst in by_acc.items():
            try:
                c = await self._client(name)
            except Exception as e:
                self.log(f'Аккаунт «{name}»: не подключился — {e}')
                continue
            for s in lst:
                self._attach(c, name, s)
            self.log(f'Аккаунт «{name}»: запущено скриптов — {len(lst)}')

    async def stop(self):
        self.running = False
        for t in self.tasks:
            t.cancel()
        self.tasks = []
        for c in self.clients.values():
            try:
                for cb, ev in list(c.list_event_handlers()):
                    c.remove_event_handler(cb, ev)
            except Exception:
                pass
            try:
                await c.disconnect()
            except Exception:
                pass
        self.clients.clear()
        self.locks.clear()
        self.log('Остановлено.')

    # ---------- общая отправка с предохранителями ----------
    async def _send(self, name, s, chat_id, text, reply_to=None):
        key = (s['name'], chat_id)
        if key in self.dead:
            return False
        c = self.clients[name]
        lock = self.locks[name]
        async with lock[0]:
            wait = ACCOUNT_GAP_S - (asyncio.get_event_loop().time() - lock[1])
            if wait > 0:
                await asyncio.sleep(wait)
            try:
                ent = await c.get_entity(int(chat_id))
                if isinstance(ent, User):               # личные сообщения запрещены в принципе
                    self.dead.add(key)
                    self.log(f'«{s["name"]}»: {chat_id} — это не чат, пропускаю навсегда.')
                    return False
                await c.send_message(ent, text, reply_to=reply_to)
                lock[1] = asyncio.get_event_loop().time()
                return True
            except errors.FloodWaitError as e:
                self.log(f'«{s["name"]}»: Telegram просит подождать {e.seconds} с — жду.')
                if e.seconds > 3600:
                    self.dead.add(key)
                    self.log(f'«{s["name"]}»: ожидание слишком долгое — чат отключён до перезапуска. Проверьте аккаунт в Telegram.')
                else:
                    await asyncio.sleep(e.seconds + 5)
            except (errors.ChatWriteForbiddenError, errors.UserBannedInChannelError, errors.ChannelPrivateError,
                    errors.ChatAdminRequiredError) as e:
                self.dead.add(key)
                self.log(f'«{s["name"]}»: в чате {chat_id} писать нельзя ({type(e).__name__}) — чат отключён.')
            except Exception as e:
                self.log(f'«{s["name"]}»: ошибка отправки — {type(e).__name__}: {e}')
        return False

    # ---------- действия ----------
    def _attach(self, client, name, s):
        chats = [int(x) for x in s['chats']]
        if s['post']['enabled']:
            self.tasks.append(self.loop.create_task(self._post_loop(name, s)))
        if s['welcome']['enabled']:
            client.add_event_handler(self._welcome_handler(name, s), events.ChatAction(chats=chats))
        if s['replies']:
            client.add_event_handler(self._reply_handler(name, s), events.NewMessage(chats=chats, incoming=True))

    async def _post_loop(self, name, s):
        cfg = s['post']
        while self.running:
            try:
                now = datetime.now(timezone.utc)
                for chat in s['chats']:
                    k = f'post|{s["name"]}|{chat}'
                    st = self.state.get(k, {})
                    if (s['name'], chat) in self.dead or not rules.post_due(st, cfg, now):
                        continue
                    await asyncio.sleep(random.uniform(1, 45))
                    text, st = rules.pick_variant(cfg['variants'], cfg['order'], st)
                    if await self._send(name, s, chat, rules.fill(text, '', ''), None):
                        self.state[k] = rules.after_post(st, cfg, datetime.now(timezone.utc))
                        self._save_state()
                        self.log(f'«{s["name"]}»: отправлено в {chat}.')
                await asyncio.sleep(30)
            except asyncio.CancelledError:
                raise
            except Exception as e:
                self.log(f'«{s["name"]}»: сбой цикла — {e}')
                await asyncio.sleep(60)

    def _welcome_handler(self, name, s):
        async def h(ev):
            try:
                if not (ev.user_joined or ev.user_added) or not self.running:
                    return
                u = await ev.get_user()
                if not u or getattr(u, 'bot', False) or getattr(u, 'is_self', False):
                    return
                k = f'welcome|{s["name"]}|{ev.chat_id}'
                ok, self.state[k] = rules.gate(self.state.get(k, {}), datetime.now(timezone.utc), rules.WELCOME_GAP_S, rules.WELCOME_MAX_PER_DAY)
                if not ok:
                    return
                await asyncio.sleep(random.uniform(4, 12))
                if await self._send(name, s, str(ev.chat_id), rules.fill(s['welcome']['text'], u.first_name or '', ''), None):
                    self.log(f'«{s["name"]}»: приветствие в {ev.chat_id}.')
            except Exception as e:
                self.log(f'«{s["name"]}»: приветствие не вышло — {e}')
        return h

    def _reply_handler(self, name, s):
        async def h(ev):
            try:
                if not self.running or ev.out:
                    return
                sender = await ev.get_sender()
                if not sender or getattr(sender, 'bot', False):
                    return
                text = rules.reply_for(s['replies'], ev.raw_text)
                if not text:
                    return
                k = f'reply|{s["name"]}|{ev.chat_id}'
                ok, self.state[k] = rules.gate(self.state.get(k, {}), datetime.now(timezone.utc), rules.REPLY_COOLDOWN_S, rules.REPLY_MAX_PER_DAY)
                if not ok:
                    return
                await asyncio.sleep(random.uniform(5, 20))
                if await self._send(name, s, str(ev.chat_id), rules.fill(text, getattr(sender, 'first_name', '') or '', ''), ev.id):
                    self.log(f'«{s["name"]}»: ответ в {ev.chat_id}.')
            except Exception as e:
                self.log(f'«{s["name"]}»: ответ не вышел — {e}')
        return h
