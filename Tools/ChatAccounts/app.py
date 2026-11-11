"""Ghost Shell · Chat Accounts — окно программы (tkinter). Логика — в rules.py и engine.py."""
import queue
import tkinter as tk
from tkinter import ttk, messagebox, filedialog

import rules
import store
from engine import Engine

VERSION = '1.0.0'
NOT_DOING = ('Чего программа НЕ делает (зашито, не настраивается): не собирает списки участников, не пишет людям в личку, '
             'не вступает в чужие чаты, не добавляет людей, не выполняет произвольный код. Скрипт — только настройки.')
BG, FG, ACC = '#0b0716', '#eaf8ff', '#00f0ff'


class App(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title(f'Ghost Shell · Chat Accounts {VERSION}')
        self.geometry('900x720')
        self.configure(bg=BG)
        self.q = queue.Queue()
        self.engine = Engine(lambda t: self.q.put(t))
        self.scripts = store.load_json('scripts.json', [])
        self.chat_map = {}          # номер -> название (для выбранного аккаунта)
        self.sel = None
        st = ttk.Style(self)
        try:
            st.theme_use('clam')
        except tk.TclError:
            pass
        st.configure('.', background=BG, foreground=FG, fieldbackground='#1a1030')
        st.configure('TNotebook', background=BG)
        st.configure('TNotebook.Tab', background='#1a1030', foreground=FG, padding=(14, 6))
        st.map('TNotebook.Tab', background=[('selected', '#2a1a55')], foreground=[('selected', ACC)])
        st.configure('TButton', background='#2a1a55', foreground=FG, padding=6)
        st.configure('TLabel', background=BG, foreground=FG)
        st.configure('TCheckbutton', background=BG, foreground=FG)
        st.configure('Dim.TLabel', foreground='#a08fe0')
        ttk.Label(self, text=NOT_DOING, style='Dim.TLabel', wraplength=860, justify='left').pack(fill='x', padx=12, pady=(8, 2))
        nb = ttk.Notebook(self)
        nb.pack(fill='both', expand=True, padx=10, pady=8)
        self.t_acc, self.t_scr, self.t_run = ttk.Frame(nb), ttk.Frame(nb), ttk.Frame(nb)
        nb.add(self.t_acc, text='Аккаунты')
        nb.add(self.t_scr, text='Скрипты')
        nb.add(self.t_run, text='Запуск')
        self._build_accounts()
        self._build_scripts()
        self._build_run()
        self.after(300, self._pump)
        self.protocol('WM_DELETE_WINDOW', self._close)

    # ---------- общие ----------
    def _field(self, parent, label, r, width=34, show=None):
        ttk.Label(parent, text=label).grid(row=r, column=0, sticky='w', padx=6, pady=3)
        v = tk.StringVar()
        ttk.Entry(parent, textvariable=v, width=width, show=show).grid(row=r, column=1, sticky='w', padx=6, pady=3)
        return v

    def _text(self, parent, h):
        t = tk.Text(parent, height=h, width=60, bg='#1a1030', fg=FG, insertbackground=FG, relief='flat', wrap='word')
        return t

    def _bg(self, fut, ok, err=None):
        def done(f):
            try:
                r = f.result()
                self.after(0, lambda: ok(r))
            except Exception as e:
                msg = str(e) or type(e).__name__
                self.after(0, lambda: (err or (lambda m: messagebox.showerror('Не вышло', m)))(msg))
        fut.add_done_callback(done)

    def _pump(self):
        try:
            while True:
                t = self.q.get_nowait()
                self.log.configure(state='normal')
                self.log.insert('end', t + '\n')
                self.log.see('end')
                self.log.configure(state='disabled')
        except queue.Empty:
            pass
        self.after(300, self._pump)

    # ---------- аккаунты ----------
    def _build_accounts(self):
        f = self.t_acc
        ttk.Label(f, text='Подключайте только СВОИ аккаунты. Данные api_id и api_hash вы получаете на my.telegram.org → API development tools '
                          '(это ваши ключи приложения, не пароль).', style='Dim.TLabel', wraplength=840, justify='left').grid(row=0, column=0, columnspan=2, sticky='w', padx=6, pady=6)
        self.a_name = self._field(f, 'Имя (для себя)', 1)
        self.a_id = self._field(f, 'api_id', 2)
        self.a_hash = self._field(f, 'api_hash', 3, show='•')
        self.a_phone = self._field(f, 'Телефон (+7…)', 4)
        ttk.Button(f, text='Отправить код в Telegram', command=self._acc_code).grid(row=5, column=1, sticky='w', padx=6, pady=4)
        self.a_code = self._field(f, 'Код из Telegram', 6)
        self.a_pw = self._field(f, 'Пароль 2FA (если есть)', 7, show='•')
        ttk.Button(f, text='Подтвердить и сохранить', command=self._acc_confirm).grid(row=8, column=1, sticky='w', padx=6, pady=4)
        ttk.Label(f, text='Подключённые аккаунты:').grid(row=9, column=0, sticky='w', padx=6, pady=(14, 2))
        self.acc_list = tk.Listbox(f, height=5, bg='#1a1030', fg=FG, relief='flat', exportselection=False)
        self.acc_list.grid(row=10, column=0, columnspan=2, sticky='we', padx=6)
        ttk.Button(f, text='Забыть выбранный аккаунт', command=self._acc_forget).grid(row=11, column=1, sticky='w', padx=6, pady=6)
        ttk.Label(f, text='Вход хранится на этом компьютере в зашифрованном виде (Windows DPAPI, только для вашей учётной записи Windows) и никуда не отправляется.',
                  style='Dim.TLabel', wraplength=840, justify='left').grid(row=12, column=0, columnspan=2, sticky='w', padx=6)
        self._refresh_accounts()

    def _refresh_accounts(self):
        self.acc_list.delete(0, 'end')
        names = list(store.list_accounts())
        for n in names:
            self.acc_list.insert('end', n)
        if hasattr(self, 's_acc'):
            self.s_acc['values'] = names

    def _acc_code(self):
        name = self.a_name.get().strip()
        try:
            if not store.ACC_RE.match(name):
                raise ValueError('имя: 1–40 знаков — буквы, цифры, пробел, точка, дефис')
            api_id = int(self.a_id.get().strip())
        except ValueError as e:
            messagebox.showerror('Проверьте поля', str(e) if 'имя' in str(e) else 'api_id — число'); return
        self._bg(self.engine.submit(self.engine.login_send_code(name, api_id, self.a_hash.get().strip(), self.a_phone.get().strip())),
                 lambda _: messagebox.showinfo('Код отправлен', 'Telegram прислал код в приложение (или SMS). Введите его ниже.'))

    def _acc_confirm(self):
        name = self.a_name.get().strip()
        def ok(r):
            if r == 'password':
                messagebox.showinfo('Нужен пароль', 'У аккаунта включён пароль 2FA — введите его в поле и нажмите ещё раз.'); return
            self.a_code.set(''); self.a_pw.set(''); self.a_hash.set('')
            self._refresh_accounts()
            messagebox.showinfo('Готово', 'Аккаунт подключён: ' + r[3:])
        self._bg(self.engine.submit(self.engine.login_confirm(name, self.a_code.get().strip(), self.a_pw.get() or None)), ok)

    def _acc_forget(self):
        s = self.acc_list.curselection()
        if not s:
            return
        n = self.acc_list.get(s[0])
        if messagebox.askyesno('Забыть аккаунт', f'Удалить вход «{n}» с этого компьютера? Сам аккаунт в Telegram не изменится.'):
            store.forget_account(n)
            self._refresh_accounts()

    # ---------- скрипты ----------
    def _build_scripts(self):
        f = self.t_scr
        left = ttk.Frame(f); left.pack(side='left', fill='y', padx=6, pady=6)
        self.scr_list = tk.Listbox(left, width=22, height=24, bg='#1a1030', fg=FG, relief='flat', exportselection=False)
        self.scr_list.pack()
        self.scr_list.bind('<<ListboxSelect>>', lambda _e: self._scr_pick())
        ttk.Button(left, text='Новый скрипт', command=self._scr_new).pack(fill='x', pady=3)
        ttk.Button(left, text='Копировать', command=self._scr_copy).pack(fill='x')
        ttk.Button(left, text='Добавить из файла…', command=self._scr_import).pack(fill='x', pady=3)
        ttk.Button(left, text='Сохранить в файл…', command=self._scr_export).pack(fill='x')
        ttk.Button(left, text='Удалить', command=self._scr_delete).pack(fill='x', pady=3)
        right = ttk.Frame(f); right.pack(side='left', fill='both', expand=True, padx=6, pady=6)
        r = 0
        self.s_name = self._field(right, 'Название', r); r += 1
        ttk.Label(right, text='Аккаунт').grid(row=r, column=0, sticky='w', padx=6)
        self.s_acc = ttk.Combobox(right, state='readonly', width=32, values=list(store.list_accounts())); self.s_acc.grid(row=r, column=1, sticky='w', padx=6); r += 1
        ttk.Button(right, text='Загрузить чаты аккаунта', command=self._scr_chats).grid(row=r, column=1, sticky='w', padx=6, pady=3); r += 1
        ttk.Label(right, text='Чаты (можно несколько)').grid(row=r, column=0, sticky='nw', padx=6)
        self.s_chats = tk.Listbox(right, height=5, selectmode='multiple', width=60, bg='#1a1030', fg=FG, relief='flat', exportselection=False)
        self.s_chats.grid(row=r, column=1, sticky='w', padx=6); r += 1
        self.v_enabled = tk.BooleanVar(value=True)
        ttk.Checkbutton(right, text='Скрипт включён', variable=self.v_enabled).grid(row=r, column=1, sticky='w', padx=6); r += 1
        self.v_post = tk.BooleanVar()
        ttk.Checkbutton(right, text='Регулярные сообщения в чат', variable=self.v_post).grid(row=r, column=0, columnspan=2, sticky='w', padx=6, pady=(8, 0)); r += 1
        ttk.Label(right, text='Тексты (каждый с новой строки)').grid(row=r, column=0, sticky='nw', padx=6)
        self.s_variants = self._text(right, 4); self.s_variants.grid(row=r, column=1, padx=6); r += 1
        row = ttk.Frame(right); row.grid(row=r, column=1, sticky='w', padx=6, pady=3)
        self.s_every = tk.StringVar(value='60'); self.s_perday = tk.StringVar(value='12'); self.s_from = tk.StringVar(value='9'); self.s_to = tk.StringVar(value='23'); self.s_tz = tk.StringVar(value='3')
        self.s_order = tk.StringVar(value='random')
        for lab, var, w in (('каждые, мин', self.s_every, 5), ('в сутки ≤', self.s_perday, 4), ('с часа', self.s_from, 3), ('до', self.s_to, 3), ('пояс UTC+', self.s_tz, 3)):
            ttk.Label(row, text=lab).pack(side='left'); ttk.Entry(row, textvariable=var, width=w).pack(side='left', padx=(2, 8))
        ttk.Combobox(row, textvariable=self.s_order, state='readonly', width=9, values=['random', 'cycle']).pack(side='left'); r += 1
        self.s_image = self._field(right, 'Картинка к сообщению (файл, необязательно)', r, width=48)
        ttk.Button(right, text='Выбрать…', command=lambda: self.s_image.set(filedialog.askopenfilename(filetypes=[('Картинки и видео', '*.jpg *.jpeg *.png *.gif *.webp *.mp4')]) or self.s_image.get())).grid(row=r, column=2, padx=4); r += 1
        ttk.Label(right, text='Ответы по словам\n(слово1, слово2 => ответ)').grid(row=r, column=0, sticky='nw', padx=6, pady=(8, 0))
        self.s_replies = self._text(right, 4); self.s_replies.grid(row=r, column=1, padx=6, pady=(8, 0)); r += 1
        ttk.Button(right, text='Сохранить скрипт', command=self._scr_save).grid(row=r, column=1, sticky='w', padx=6, pady=8)
        ttk.Label(right, text=f'Лимиты: сообщения не чаще раза в {rules.MIN_EVERY_MIN} мин. и не больше {rules.MAX_PER_DAY} в сутки на чат; ответы — раз в минуту; всё с задержками.',
                  style='Dim.TLabel', wraplength=520, justify='left').grid(row=r + 1, column=0, columnspan=2, sticky='w', padx=6)
        self._scr_refresh()

    def _scr_refresh(self):
        self.scr_list.delete(0, 'end')
        for s in self.scripts:
            self.scr_list.insert('end', ('● ' if s.get('enabled', True) else '○ ') + s['name'])
        if hasattr(self, 's_acc'):
            self.s_acc['values'] = list(store.list_accounts())

    def _scr_new(self):
        self.sel = None; self.scr_list.selection_clear(0, 'end')
        self._scr_fill(rules.default_script())

    def _scr_fill(self, s):
        self.s_name.set(s['name']); self.s_acc.set(s['account'])
        self.s_chats.delete(0, 'end')
        for c in s['chats']:
            self.s_chats.insert('end', f'{self.chat_map.get(c, c)}  [{c}]'); self.s_chats.selection_set('end')
        self.v_enabled.set(s.get('enabled', True)); self.v_post.set(s['post']['enabled'])
        p = s['post']
        self.s_variants.delete('1.0', 'end'); self.s_variants.insert('1.0', '\n'.join(p['variants']))
        self.s_every.set(p['everyMin']); self.s_perday.set(p['perDay']); self.s_from.set(p['from']); self.s_to.set(p['to']); self.s_tz.set(p['tz']); self.s_order.set(p['order'])
        self.s_image.set(p.get('image', ''))
        self.s_replies.delete('1.0', 'end')
        self.s_replies.insert('1.0', '\n'.join(', '.join(r['keywords']) + ' => ' + r['text'] for r in s['replies']))

    def _scr_pick(self):
        s = self.scr_list.curselection()
        if s:
            self.sel = s[0]; self._scr_fill(self.scripts[s[0]])

    def _scr_chats(self):
        acc = self.s_acc.get()
        if not acc:
            messagebox.showinfo('Аккаунт', 'Сначала выберите аккаунт.'); return
        def ok(lst):
            keep = {self._chat_id(self.s_chats.get(i)) for i in self.s_chats.curselection()}
            self.chat_map = dict(lst)
            self.s_chats.delete(0, 'end')
            for cid, title in lst:
                self.s_chats.insert('end', f'{title}  [{cid}]')
                if cid in keep:
                    self.s_chats.selection_set('end')
        self._bg(self.engine.submit(self.engine.list_chats(acc)), ok)

    @staticmethod
    def _chat_id(item):
        return item.rsplit('[', 1)[-1].rstrip(']')

    def _scr_collect(self):
        replies = []
        for line in self.s_replies.get('1.0', 'end').splitlines():
            if '=>' in line:
                k, t = line.split('=>', 1)
                replies.append({'keywords': [x.strip() for x in k.split(',')], 'text': t.strip()})
        return {'name': self.s_name.get().strip(), 'account': self.s_acc.get(), 'enabled': self.v_enabled.get(),
                'chats': [self._chat_id(self.s_chats.get(i)) for i in self.s_chats.curselection()],
                'post': {'enabled': self.v_post.get(), 'variants': self.s_variants.get('1.0', 'end').splitlines(), 'order': self.s_order.get(),
                         'everyMin': self.s_every.get(), 'perDay': self.s_perday.get(), 'from': self.s_from.get(), 'to': self.s_to.get(), 'tz': self.s_tz.get(), 'image': self.s_image.get().strip()},
                'replies': replies}

    def _scr_save(self):
        try:
            s = rules.validate(self._scr_collect())
        except rules.ScriptError as e:
            messagebox.showerror('Проверьте скрипт', str(e)); return
        if self.sel is None:
            self.scripts.append(s); self.sel = len(self.scripts) - 1
        else:
            self.scripts[self.sel] = s
        store.save_json('scripts.json', self.scripts)
        self._scr_refresh()
        messagebox.showinfo('Сохранено', 'Скрипт сохранён. Чтобы он заработал, нажмите «Запустить» на вкладке «Запуск».')

    def _scr_copy(self):
        if self.sel is None:
            return
        import copy
        s = copy.deepcopy(self.scripts[self.sel]); s['name'] = (s['name'] + ' копия')[:40]
        self.scripts.append(s); store.save_json('scripts.json', self.scripts); self._scr_refresh()

    def _scr_export(self):
        if self.sel is None:
            return
        p = filedialog.asksaveasfilename(defaultextension='.json', initialfile=self.scripts[self.sel]['name'] + '.json', filetypes=[('Скрипт', '*.json')])
        if p:
            import json
            with open(p, 'w', encoding='utf-8') as f:
                json.dump(self.scripts[self.sel], f, ensure_ascii=False, indent=1)

    def _scr_import(self):
        # файл скрипта — только настройки: всё лишнее выбрасывается, лимиты применяются так же, как при ручном вводе
        import json
        p = filedialog.askopenfilename(filetypes=[('Скрипт', '*.json')])
        if not p:
            return
        try:
            with open(p, 'rb') as f:
                raw = f.read(200_000)
            s = rules.validate(json.loads(raw.decode('utf-8')))
        except (OSError, ValueError) as e:
            messagebox.showerror('Не вышло добавить', f'Файл не подошёл: {e}'); return
        if s['account'] not in store.list_accounts():
            messagebox.showinfo('Аккаунт', f'В скрипте указан аккаунт «{s["account"]}», его здесь нет. Откройте скрипт и выберите свой аккаунт и чаты.')
        self.scripts.append(s); store.save_json('scripts.json', self.scripts); self._scr_refresh()

    def _scr_delete(self):
        if self.sel is not None and messagebox.askyesno('Удалить', 'Удалить выбранный скрипт?'):
            del self.scripts[self.sel]; self.sel = None
            store.save_json('scripts.json', self.scripts); self._scr_refresh(); self._scr_fill(rules.default_script())

    # ---------- запуск ----------
    def _build_run(self):
        f = self.t_run
        bar = ttk.Frame(f); bar.pack(fill='x', padx=8, pady=8)
        ttk.Button(bar, text='▶ Запустить', command=self._start).pack(side='left')
        ttk.Button(bar, text='■ Остановить', command=self._stop).pack(side='left', padx=8)
        self.log = tk.Text(f, bg='#0b0716', fg=FG, relief='flat', state='disabled', wrap='word')
        self.log.pack(fill='both', expand=True, padx=8, pady=(0, 8))
        self.q.put('Готово. Добавьте аккаунт, создайте скрипт и нажмите «Запустить». Программа работает, пока открыта (окно можно свернуть).')

    def _start(self):
        valid = []
        for s in self.scripts:
            try:
                valid.append(rules.validate(s))
            except rules.ScriptError as e:
                self.q.put(f'Скрипт «{s.get("name", "?")}» пропущен: {e}')
        self._bg(self.engine.submit(self.engine.start(valid)), lambda _: None)

    def _stop(self):
        self._bg(self.engine.submit(self.engine.stop()), lambda _: None)

    def _close(self):
        try:
            self.engine.submit(self.engine.stop()).result(8)
        except Exception:
            pass
        self.destroy()


if __name__ == '__main__':
    App().mainloop()
