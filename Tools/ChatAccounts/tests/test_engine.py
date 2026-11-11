"""Движок с подставным клиентом Telegram: проверяем границы (личка запрещена, мёртвые чаты, лимиты), без сети."""
import asyncio, os, sys, tempfile, unittest
from datetime import datetime, timezone
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
os.environ['GS_ALLOW_PLAIN'] = '1'
import engine as E, rules, store
from telethon.tl.types import User, Chat
from telethon import errors

class FakeClient:
    def __init__(self, ents): self.ents = ents; self.sent = []; self.fail = None
    async def get_entity(self, i): return self.ents[i]
    async def send_message(self, ent, text, reply_to=None):
        if self.fail: raise self.fail
        self.sent.append((ent, text, reply_to))

def mk():
    store.DIR = tempfile.mkdtemp()
    logs = []
    e = E.Engine(logs.append); e.running = True
    return e, logs

def script():
    return rules.validate({'name': 's', 'account': 'a', 'chats': ['-1001234567', '555555'],
                           'post': {'enabled': True, 'variants': ['hi'], 'everyMin': 10, 'perDay': 5, 'from': 0, 'to': 24, 'tz': 0}})

class T(unittest.TestCase):
    def run_(self, e, coro): return asyncio.run_coroutine_threadsafe(coro, e.loop).result(30)
    def setup_(self, e, fc):
        e.clients['a'] = fc; e.locks['a'] = [asyncio.Lock(), 0.0]
    def test_private_chat_refused(self):
        e, logs = mk(); E.ACCOUNT_GAP_S = 0
        fc = FakeClient({555555: User(id=555555, is_self=False)}); self.setup_(e, fc)
        ok = self.run_(e, e._send('a', script(), '555555', 'x'))
        self.assertFalse(ok); self.assertEqual(fc.sent, []); self.assertIn(('s', '555555'), e.dead)
    def test_group_send_ok(self):
        e, logs = mk(); E.ACCOUNT_GAP_S = 0
        fc = FakeClient({-1001234567: Chat(id=1234567, title='g', photo=None, participants_count=1, date=None, version=1)}); self.setup_(e, fc)
        self.assertTrue(self.run_(e, e._send('a', script(), '-1001234567', 'hello')))
        self.assertEqual(fc.sent[0][1], 'hello')
    def test_forbidden_disables_chat(self):
        e, logs = mk(); E.ACCOUNT_GAP_S = 0
        fc = FakeClient({-1001234567: Chat(id=1234567, title='g', photo=None, participants_count=1, date=None, version=1)}); fc.fail = errors.ChatWriteForbiddenError(request=None)
        self.setup_(e, fc)
        self.assertFalse(self.run_(e, e._send('a', script(), '-1001234567', 'x')))
        self.assertIn(('s', '-1001234567'), e.dead)
        fc.fail = None
        self.assertFalse(self.run_(e, e._send('a', script(), '-1001234567', 'x')))   # больше не пробует
        self.assertEqual(fc.sent, [])
    def test_no_dm_api_in_engine(self):
        src = open(os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'engine.py'), encoding='utf-8').read()
        for forbidden in ('GetParticipants', 'iter_participants', 'JoinChannel', 'ImportChatInvite', 'AddChatUser', 'InviteToChannel', 'eval(', 'exec('):
            self.assertNotIn(forbidden, src)

if __name__ == '__main__': unittest.main()
