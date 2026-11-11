import os, sys, unittest
from datetime import datetime, timezone, timedelta
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
os.environ['GS_ALLOW_PLAIN'] = '1'
import rules, store

def base(**kw):
    s = {'name': 'тест', 'account': 'main', 'chats': ['-1001234567890'],
         'post': {'enabled': True, 'variants': ['а', 'б'], 'everyMin': 60, 'perDay': 5, 'from': 9, 'to': 23, 'tz': 3}}
    s.update(kw); return s

class T(unittest.TestCase):
    def test_valid(self):
        s = rules.validate(base()); self.assertEqual(s['post']['everyMin'], 60)
    def test_limits(self):
        for bad in ({'everyMin': 5}, {'perDay': 500}, {'everyMin': 'x'}):
            b = base(); b['post'] = {**b['post'], **bad}
            with self.assertRaises(rules.ScriptError): rules.validate(b)
    def test_chat_ids(self):
        for c in (['@user'], ['12'], ['abc'], []):
            with self.assertRaises(rules.ScriptError): rules.validate(base(chats=c))
    def test_no_action(self):
        with self.assertRaises(rules.ScriptError): rules.validate(base(post={'enabled': False}))
    def test_extra_fields_dropped(self):
        s = rules.validate(base(code='import os', exec='x', users=['1']))
        self.assertNotIn('code', s); self.assertNotIn('exec', s); self.assertNotIn('users', s)
    def test_text_cleaned(self):
        b = base(); b['post']['variants'] = ['‮привет\x00']
        self.assertEqual(rules.validate(b)['post']['variants'], ['привет'])
    def test_hours_midnight(self):
        c = {'from': 22, 'to': 6, 'tz': 0}
        self.assertTrue(rules.in_hours(datetime(2026,1,1,23,0,tzinfo=timezone.utc), c))
        self.assertTrue(rules.in_hours(datetime(2026,1,1,3,0,tzinfo=timezone.utc), c))
        self.assertFalse(rules.in_hours(datetime(2026,1,1,12,0,tzinfo=timezone.utc), c))
    def test_post_due_and_cap(self):
        cfg = rules.validate(base())['post']; cfg['perDay'] = 2
        t = datetime(2026,1,1,12,0,tzinfo=timezone.utc); st = {}
        self.assertTrue(rules.post_due(st, cfg, t)); st = rules.after_post(st, cfg, t)
        self.assertFalse(rules.post_due(st, cfg, t + timedelta(minutes=30)))
        t2 = t + timedelta(minutes=61); self.assertTrue(rules.post_due(st, cfg, t2)); st = rules.after_post(st, cfg, t2)
        self.assertFalse(rules.post_due(st, cfg, t2 + timedelta(hours=3)))          # лимит в сутки
        self.assertTrue(rules.post_due(st, cfg, t + timedelta(days=1)))             # новые сутки
        self.assertFalse(rules.post_due({}, cfg, datetime(2026,1,1,1,0,tzinfo=timezone.utc)))  # тихие часы
    def test_variants_no_repeat_and_cycle(self):
        st = {}; seq = []
        for _ in range(30):
            v, st = rules.pick_variant(['а','б','в'], 'random', st); seq.append(v)
        self.assertTrue(all(a != b for a, b in zip(seq, seq[1:])))
        st = {}; seq = []
        for _ in range(4):
            v, st = rules.pick_variant(['а','б','в'], 'cycle', st); seq.append(v)
        self.assertEqual(seq, ['а','б','в','а'])
    def test_gate(self):
        t = datetime(2026,1,1,12,0,tzinfo=timezone.utc)
        ok, st = rules.gate({}, t, 60, 2); self.assertTrue(ok)
        ok, st2 = rules.gate(st, t + timedelta(seconds=10), 60, 2); self.assertFalse(ok)
        ok, st = rules.gate(st, t + timedelta(seconds=70), 60, 2); self.assertTrue(ok)
        ok, _ = rules.gate(st, t + timedelta(seconds=200), 60, 2); self.assertFalse(ok)
    def test_replies(self):
        s = rules.validate(base(replies=[{'keywords': ['Цена', 'x'], 'text': 'ответ'}]))
        self.assertEqual(s['replies'][0]['keywords'], ['цена'])
        self.assertEqual(rules.reply_for(s['replies'], 'А какая ЦЕНА?'), 'ответ')
        self.assertIsNone(rules.reply_for(s['replies'], 'привет'))
    def test_image_and_welcome(self):
        b = base(); b['post']['image'] = 'C:/pics/a.PNG'
        self.assertEqual(rules.validate(b)['post']['image'], 'C:/pics/a.PNG')
        b['post']['image'] = 'run.exe'
        with self.assertRaises(rules.ScriptError): rules.validate(b)
        self.assertNotIn('welcome', rules.validate(base(welcome={'enabled': True, 'text': 'x'})))
    def test_phone(self):
        self.assertEqual(rules.build_phone('7', '(912) 345-67-89'), '+79123456789')
        self.assertEqual(rules.build_phone('+49', '151 2345 6789'), '+4915123456789')
        self.assertEqual(rules.build_phone('380', '380501234567'), '+380501234567')   # код уже в номере
        self.assertEqual(rules.build_phone('1', '2025550123'), '+12025550123')
        for c, n in (('', '123456789'), ('7', '12'), ('abcd', '123456789')):
            with self.assertRaises(rules.ScriptError): rules.build_phone(c, n)
    def test_fill(self):
        self.assertEqual(rules.fill('Привет, {name}!', 'Аня‮'), 'Привет, Аня!')
    def test_store_roundtrip(self):
        import tempfile
        store.DIR = tempfile.mkdtemp()
        store.save_account('main', 123, 'hash', '+10000000000', 'SESSION')
        self.assertEqual(store.load_secret('main')['session'], 'SESSION')
        self.assertIn('main', store.list_accounts())
        with open(os.path.join(store.DIR, 'accounts', 'main.dat'), 'rb') as f:
            self.assertNotIn(b'hash', f.read()) if os.name == 'nt' else None
        with self.assertRaises(ValueError): store.load_secret('../x')
        store.forget_account('main'); self.assertNotIn('main', store.list_accounts())

if __name__ == '__main__': unittest.main()
