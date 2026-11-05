using System.IO;
using System.Text;

namespace BattleLauncher;

enum OpState { Queued, Running, Done, Failed, Cancelled }

// Снимок одной операции для окна: копия, которую можно спокойно читать из любого потока.
record OpView(int Seq, string Key, string Title, string Kind, OpState State, string Phase, int? Percent, string? Error, DateTime Added, DateTime? Started, DateTime? Finished);

// Одна операция в менеджере: обновление, установка, удаление, запуск. Объект отдаётся самой операции, чтобы она рассказывала, что делает.
sealed class OpItem
{
    internal readonly OpQueue Owner;
    internal int Seq;
    internal string Key = "", Title = "", Kind = "";
    internal OpState State = OpState.Queued;
    internal string Phase = "В очереди";
    internal int? Percent;
    internal string? Error;
    internal string? NoteText;
    internal DateTime Added = DateTime.Now;
    internal DateTime? Started, Finished;
    internal Func<OpItem, CancellationToken, Task<bool>> Run = null!;
    internal readonly CancellationTokenSource Cts = new();
    internal readonly TaskCompletionSource<bool> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal OpItem(OpQueue owner) { Owner = owner; }

    // Что делается прямо сейчас: «Останавливаю плагин…», «Заменяю файл…», «Запускаю снова…».
    public void SetPhase(string text) => Owner.Update(this, i => { if (i.Phase == text) return false; i.Phase = text; return true; });

    // Процент скачивания (null — процент неизвестен, идёт установка).
    public void SetPercent(int? percent)
    {
        Owner.Update(this, i =>
        {
            if (i.Percent == percent) return false;
            i.Percent = percent;
            if (percent != null) i.Phase = $"Скачиваю {percent}%";
            return true;
        });
    }

    // Пометка к успешному итогу: «Готово — запущен снова».
    public void SetNote(string text) => Owner.Update(this, i => { i.NoteText = text; return false; });

    // Причина неудачи простыми словами: показывается в списке (операция при этом возвращает false).
    public void Fail(string reason) => Owner.Update(this, i => { i.Error = reason; return true; });
}

// Очередь операций «по одной»: все установки и обновления идут строго друг за другом (два плагина, которые меняют панель задач,
// не должны останавливаться и запускаться одновременно). Окно получает только снимки (Snapshot) и сигнал Changed.
sealed class OpQueue
{
    public const int KeepFinished = 40;
    readonly object gate = new();
    readonly List<OpItem> items = new();
    readonly Action<Action>? post;
    bool pumping;
    int seq;

    // post — как доставить сигнал Changed в поток окна (в самопроверке без окна — null, сигнал приходит сразу).
    public OpQueue(Action<Action>? post = null) { this.post = post; }

    public event Action? Changed;

    void Raise()
    {
        var h = Changed;
        if (h == null) return;
        if (post != null) post(() => Changed?.Invoke()); else h();
    }

    internal void Update(OpItem it, Func<OpItem, bool> change)
    {
        bool changed;
        lock (gate) changed = change(it);
        if (changed) Raise();   // сигнал только если что-то правда изменилось (процент скачивания приходит сотни раз)
    }

    public bool IsActive(string key)
    {
        lock (gate) return items.Any(i => i.Key == key && (i.State == OpState.Queued || i.State == OpState.Running));
    }

    public OpView[] Snapshot()
    {
        lock (gate) return items.Select(View).ToArray();
    }

    static OpView View(OpItem i) => new(i.Seq, i.Key, i.Title, i.Kind, i.State, i.Phase, i.Percent, i.Error, i.Added, i.Started, i.Finished);

    // Ставит операцию в очередь. Если такая уже ждёт или выполняется (повторное нажатие кнопки) — возвращает её же, дубля не будет.
    // Результат: true — операция удалась.
    public Task<bool> Enqueue(string key, string title, string kind, Func<OpItem, CancellationToken, Task<bool>> run)
    {
        OpItem it;
        lock (gate)
        {
            var same = items.FirstOrDefault(i => i.Key == key && (i.State == OpState.Queued || i.State == OpState.Running));
            if (same != null) return same.Result.Task;
            it = new OpItem(this) { Seq = ++seq, Key = key, Title = title, Kind = kind, Run = run };
            items.Add(it);
            Trim();
            if (!pumping) { pumping = true; _ = Pump(); }
        }
        Raise();
        return it.Result.Task;
    }

    // Мгновенное действие (например, запуск программы), которое просто должно остаться в списке.
    public void Record(string kind, string title, bool ok, string note)
    {
        lock (gate)
        {
            var now = DateTime.Now;
            items.Add(new OpItem(this)
            {
                Seq = ++seq, Key = "", Title = title, Kind = kind, State = ok ? OpState.Done : OpState.Failed,
                Phase = ok ? note : "Не вышло", Error = ok ? null : note, Added = now, Started = now, Finished = now,
            });
            Trim();
        }
        Raise();
    }

    // Отменяет операцию, которая ещё ждёт в очереди (уже идущую не трогаем: она на полпути меняет файлы).
    public bool Cancel(int seqNo)
    {
        OpItem? it;
        lock (gate)
        {
            it = items.FirstOrDefault(i => i.Seq == seqNo && i.State == OpState.Queued);
            if (it == null) return false;
            MarkCancelled(it);
        }
        it.Result.TrySetResult(false);
        Raise();
        return true;
    }

    public int CancelQueued()
    {
        List<OpItem> hit;
        lock (gate)
        {
            hit = items.Where(i => i.State == OpState.Queued).ToList();
            foreach (var i in hit) MarkCancelled(i);
        }
        foreach (var i in hit) i.Result.TrySetResult(false);
        if (hit.Count > 0) Raise();
        return hit.Count;
    }

    static void MarkCancelled(OpItem i)
    {
        i.State = OpState.Cancelled;
        i.Phase = "Отменено";
        i.Finished = DateTime.Now;
    }

    // Убирает из списка всё законченное (удалось, не вышло, отменено).
    public void ClearFinished()
    {
        lock (gate) items.RemoveAll(i => i.State is OpState.Done or OpState.Failed or OpState.Cancelled);
        Raise();
    }

    void Trim()
    {
        var finished = items.Where(i => i.State is OpState.Done or OpState.Failed or OpState.Cancelled).ToList();
        for (var k = 0; k < finished.Count - KeepFinished; k++) items.Remove(finished[k]);
    }

    async Task Pump()
    {
        await Task.Yield();   // сначала даём поставить в очередь всё, что ставится в одном нажатии, — потом начинаем по одному
        while (true)
        {
            OpItem? next;
            lock (gate)
            {
                next = items.FirstOrDefault(i => i.State == OpState.Queued);
                if (next == null) { pumping = false; return; }
                next.State = OpState.Running;
                next.Started = DateTime.Now;
                next.Phase = "Начинаю…";
            }
            Raise();
            var ok = false;
            var cancelled = false;
            string? err = null;
            try { ok = await next.Run(next, next.Cts.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            catch (Exception ex) { err = ex.Message; }
            lock (gate)
            {
                next.Finished = DateTime.Now;
                next.Percent = null;
                if (ok)
                {
                    next.State = OpState.Done;
                    next.Phase = next.Kind switch { "Удаление" => "Удалено", "Установка" => "Установлено", _ => "Готово" } + (next.NoteText != null ? " — " + next.NoteText : "");
                    next.Error = null;
                }
                else if (cancelled) { next.State = OpState.Cancelled; next.Phase = "Отменено"; }
                else { next.State = OpState.Failed; next.Phase = "Не вышло"; next.Error ??= err ?? "не получилось"; }
            }
            next.Result.TrySetResult(ok);
            Raise();
        }
    }
}

delegate void CheckFn(string name, bool ok, string extra = "");

// Проверка очереди без окна: порядок «по одной», дубли, отмена, ошибки, подписи фаз, очистка (Windows-раннер GitHub, сценарий browser).
static class OpsSelfTest
{
    public static void Run(string dir)
    {
        Directory.CreateDirectory(dir);
        var report = new StringBuilder();
        var bad = 0;
        void Check(string name, bool ok, string extra = "")
        {
            report.AppendLine((ok ? "OK    " : "ОШИБКА ") + name + (extra.Length > 0 ? " — " + extra : ""));
            if (!ok) bad++;
        }
        try { Body(Check); }
        catch (Exception ex) { Check("самопроверка не упала", false, ex.ToString()); }
        report.AppendLine(bad == 0 ? "ВСЁ В ПОРЯДКЕ" : $"ОШИБОК: {bad}");
        File.WriteAllText(Path.Combine(dir, "report.txt"), report.ToString());
        Environment.ExitCode = bad == 0 ? 0 : 1;
    }

    static void Body(CheckFn check)
    {
        var q = new OpQueue();
        var changes = 0;
        q.Changed += () => Interlocked.Increment(ref changes);
        var running = 0;
        var maxRunning = 0;
        var order = new List<string>();
        var gate = new object();
        var release = new ManualResetEventSlim(false);

        Func<OpItem, CancellationToken, Task<bool>> Make(string name, bool ok = true, string? failWith = null, bool waitRelease = false) => async (item, ct) =>
        {
            lock (gate) { running++; maxRunning = Math.Max(maxRunning, running); order.Add(name); }
            item.SetPhase("Останавливаю плагин…");
            if (waitRelease) { item.SetNote("запущен снова"); await Task.Run(() => release.Wait(10000)); }
            item.SetPercent(40);
            await Task.Delay(30);
            item.SetPhase("Заменяю файл…");
            lock (gate) running--;
            if (failWith != null) throw new InvalidOperationException(failWith);
            if (!ok) item.Fail("нет доступа");
            return ok;
        };

        var t1 = q.Enqueue("a", "Плагин А", "Обновление", Make("a", waitRelease: true));
        var t2 = q.Enqueue("b", "Плагин Б", "Обновление", Make("b", failWith: "Закрой «Плагин Б» и повтори."));
        var t3 = q.Enqueue("c", "Плагин В", "Установка", Make("c"));
        var t4 = q.Enqueue("d", "Плагин Г", "Обновление", Make("d", ok: false));
        var dup = q.Enqueue("a", "Плагин А", "Обновление", Make("a-дубль"));
        check("повторное нажатие не создаёт дубль", ReferenceEquals(t1, dup) && q.Snapshot().Count(v => v.Key == "a") == 1);
        check("в очереди сразу видно всё", q.Snapshot().Length == 4 && q.IsActive("c"));

        // ждём, пока первая реально начнётся (очередь стартует чуть позже, чем всё поставлено)
        for (var w = 0; w < 200 && q.Snapshot().First(v => v.Key == "a").State != OpState.Running; w++) Thread.Sleep(10);

        // отмена того, что ещё ждёт
        var cancelled = q.Cancel(q.Snapshot().First(v => v.Key == "c").Seq);
        check("ожидающую операцию можно отменить", cancelled && q.Snapshot().First(v => v.Key == "c").State == OpState.Cancelled);
        check("отменённая завершается как «не удалось»", t3.Wait(2000) && !t3.Result);
        check("идущую операцию отменить нельзя", !q.Cancel(q.Snapshot().First(v => v.Key == "a").Seq) && q.Snapshot().First(v => v.Key == "a").State == OpState.Running);

        // пока первая держит очередь, остальные ждут
        Thread.Sleep(150);
        var snap = q.Snapshot();
        check("пока идёт первая, вторая ждёт", snap.First(v => v.Key == "a").State == OpState.Running && snap.First(v => v.Key == "b").State == OpState.Queued, string.Join(",", snap.Select(v => v.Key + ":" + v.State)));
        check("у идущей видно, что она делает", snap.First(v => v.Key == "a").Phase.Contains("Останавливаю") || snap.First(v => v.Key == "a").Phase.Contains("Скачиваю") || snap.First(v => v.Key == "a").Phase.Contains("Заменяю"), snap.First(v => v.Key == "a").Phase);

        release.Set();
        check("первая удалась", t1.Wait(5000) && t1.Result);
        check("вторая с исключением — не удалась, очередь не встала", t2.Wait(5000) && !t2.Result);
        check("четвёртая (вернула false) — не удалась", t4.Wait(5000) && !t4.Result);

        var fin = q.Snapshot();
        check("порядок: по одной, как поставили", string.Join(",", order) == "a,b,d", string.Join(",", order));
        check("одновременно не больше одной", maxRunning == 1, "максимум " + maxRunning);
        check("у упавшей есть причина", fin.First(v => v.Key == "b").State == OpState.Failed && (fin.First(v => v.Key == "b").Error ?? "").Contains("Плагин Б"), fin.First(v => v.Key == "b").Error ?? "");
        check("у вернувшей false есть причина", fin.First(v => v.Key == "d").Error == "нет доступа", fin.First(v => v.Key == "d").Error ?? "");
        check("удавшаяся помечена «Готово — запущен снова»", fin.First(v => v.Key == "a").State == OpState.Done && fin.First(v => v.Key == "a").Phase == "Готово — запущен снова", fin.First(v => v.Key == "a").Phase);
        check("после завершения ключ свободен", !q.IsActive("a") && !q.IsActive("b"));
        check("сигналов об изменениях хватает", Volatile.Read(ref changes) >= 10, "сигналов: " + changes);

        // мгновенная запись и очистка
        q.Record("Запуск", "Плагин Д", true, "Запущено");
        q.Record("Запуск", "Плагин Е", false, "файл не найден");
        var rec = q.Snapshot();
        check("записанные действия видны", rec.Any(v => v.Title == "Плагин Д" && v.State == OpState.Done) && rec.Any(v => v.Title == "Плагин Е" && v.State == OpState.Failed && v.Error == "файл не найден"));
        q.ClearFinished();
        check("«Очистить» убирает всё законченное", q.Snapshot().Length == 0);

        // повторное добавление после очистки, отмена всей очереди
        var gateRelease = new ManualResetEventSlim(false);
        var first = q.Enqueue("x", "Х", "Обновление", async (it, ct) => { await Task.Run(() => gateRelease.Wait(10000)); return true; });
        var second = q.Enqueue("y", "Y", "Обновление", (it, ct) => Task.FromResult(true));
        var third = q.Enqueue("z", "Z", "Обновление", (it, ct) => Task.FromResult(true));
        Thread.Sleep(100);
        var n = q.CancelQueued();
        check("«Отменить очередь» отменяет ждущих, но не идущую", n == 2 && q.Snapshot().First(v => v.Key == "x").State == OpState.Running, "отменено " + n);
        gateRelease.Set();
        check("идущая спокойно дорабатывает", first.Wait(5000) && first.Result && second.Wait(2000) && !second.Result && third.Wait(2000) && !third.Result);

        // ограничение длины истории
        for (var k = 0; k < OpQueue.KeepFinished + 10; k++) q.Record("Запуск", "П" + k, true, "Запущено");
        check("история не растёт без конца", q.Snapshot().Length <= OpQueue.KeepFinished + 3, "записей " + q.Snapshot().Length);
    }
}
