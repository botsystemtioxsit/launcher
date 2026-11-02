using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace BattleLauncher;

// «Написать нам»: человек описывает ошибку или предложение, прикладывает до трёх скриншотов — сообщение уходит владельцу
// (в админ-панель, приложение «Сообщения в лаунчере»). Всё, что человек отправляет, видно ему до отправки.
public partial class MainWindow
{
    string fbType = "bug";
    readonly List<(string Url, BitmapSource Thumb)> fbShots = new();
    readonly Dictionary<string, Button> fbTypeButtons = new();
    TextBox? fbText, fbContact;
    CheckBox? fbSystem, fbAccount;
    TextBlock? fbHint, fbResult, fbSysInfo;
    Button? fbSend;
    WrapPanel? fbShotsBox;
    StackPanel? fbSentBox;
    bool fbBuilt, fbSending;
    DateTime fbLastSent = DateTime.MinValue;

    void OpenFeedback()
    {
        if (!fbBuilt) { BuildFeedbackView(); fbBuilt = true; }
        UpdateFeedbackAccountBox();
        UpdateFeedbackSystem();
        _ = RefreshFeedbackSent();
    }

    static Border FbCard(UIElement child) => new()
    {
        Background = Res("Panel"), BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
        Padding = new Thickness(22), Margin = new Thickness(0, 0, 0, 14), Child = child,
    };

    static TextBlock FbLabel(string text) => new() { Text = text, Foreground = Res("Muted"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };

    void BuildFeedbackView()
    {
        FeedbackBody.Children.Clear();
        FeedbackBody.Children.Add(new TextBlock { Text = "Написать нам", FontSize = 22, FontWeight = FontWeights.SemiBold });
        FeedbackBody.Children.Add(new TextBlock
        {
            Text = "Нашли ошибку или хотите что-то предложить? Напишите — сообщение придёт напрямую владельцу проекта, а ответ появится здесь же, внизу.",
            Foreground = Res("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 16, 16),
        });

        var st = new StackPanel();
        st.Children.Add(FbLabel("О чём сообщение"));
        var types = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        foreach (var (key, label) in new[] { ("bug", "Ошибка"), ("idea", "Предложение"), ("other", "Другое") })
        {
            var b = new Button { Content = label, Margin = new Thickness(types.Children.Count == 0 ? 0 : 8, 0, 0, 0) };
            var k = key;
            b.Click += (_, _) => SetFeedbackType(k);
            fbTypeButtons[key] = b;
            types.Children.Add(b);
        }
        st.Children.Add(types);

        fbHint = FbLabel("");
        st.Children.Add(fbHint);
        fbText = new TextBox { MinHeight = 140, MaxLength = Feedback.MaxText, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(8) };
        fbText.TextChanged += (_, _) => UpdateFeedbackState();
        st.Children.Add(fbText);

        st.Children.Add(new TextBlock { Text = "Скриншоты (до 3)", Foreground = Res("Muted"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 6) });
        var shotButtons = new StackPanel { Orientation = Orientation.Horizontal };
        shotButtons.Children.Add(MakeButton("Добавить файл…", null, () => { FeedbackAddFile(); return Task.CompletedTask; }));
        shotButtons.Children.Add(MakeButton("Снять экран", null, FeedbackCaptureScreen));
        ((Button)shotButtons.Children[0]).Margin = new Thickness(0);
        st.Children.Add(shotButtons);
        st.Children.Add(new TextBlock { Text = "«Снять экран»: окно лаунчера на секунду свернётся, снимок появится ниже.", Foreground = Res("Muted"), FontSize = 12, Margin = new Thickness(0, 6, 0, 0) });
        fbShotsBox = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        st.Children.Add(fbShotsBox);

        st.Children.Add(new TextBlock { Text = "Как с вами связаться (необязательно)", Foreground = Res("Muted"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 6) });
        fbContact = new TextBox { MaxLength = Feedback.MaxContact, Padding = new Thickness(8) };
        st.Children.Add(fbContact);

        fbAccount = new CheckBox { Margin = new Thickness(0, 14, 0, 0), IsChecked = true, Foreground = Res("Text") };
        st.Children.Add(fbAccount);
        fbSystem = new CheckBox { Content = "Приложить сведения о системе — помогают найти причину", IsChecked = true, Margin = new Thickness(0, 8, 0, 0), Foreground = Res("Text") };
        fbSystem.Click += (_, _) => UpdateFeedbackSystem();
        st.Children.Add(fbSystem);
        fbSysInfo = new TextBlock { Foreground = Res("Muted"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(22, 4, 0, 0) };
        st.Children.Add(fbSysInfo);

        fbSend = MakeButton("Отправить", "Primary", FeedbackSend);
        fbSend.Margin = new Thickness(0, 18, 0, 0);
        fbSend.HorizontalAlignment = HorizontalAlignment.Left;
        st.Children.Add(fbSend);
        fbResult = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
        st.Children.Add(fbResult);
        FeedbackBody.Children.Add(FbCard(st));

        FeedbackBody.Children.Add(new TextBlock { Text = "ВАШИ СООБЩЕНИЯ", FontFamily = (FontFamily)FindResource("Mono"), FontWeight = FontWeights.Bold, Foreground = Res("Accent2"), Margin = new Thickness(0, 6, 0, 8) });
        fbSentBox = new StackPanel();
        FeedbackBody.Children.Add(fbSentBox);

        SetFeedbackType("bug");
    }

    void SetFeedbackType(string type)
    {
        fbType = type;
        foreach (var (k, b) in fbTypeButtons) b.Style = (Style)FindResource(k == type ? "Primary" : "Ghost");
        if (fbHint != null)
            fbHint.Text = type == "bug" ? "Что случилось? Опишите: что делали, что ожидали и что получилось."
                        : type == "idea" ? "Что предлагаете добавить или изменить?"
                        : "Ваше сообщение";
        UpdateFeedbackState();
    }

    // подпись аккаунта и сведения о системе — показываем человеку ровно то, что уйдёт
    void UpdateFeedbackAccountBox()
    {
        if (fbAccount == null) return;
        var me = store.Active;
        var can = me != null && !me.IsToken;
        fbAccount.Visibility = can ? Visibility.Visible : Visibility.Collapsed;
        if (can) fbAccount.Content = "Подписать сообщение моим аккаунтом (" + me!.Name + ") — так вам смогут ответить";
        UpdateFeedbackState();
    }

    void UpdateFeedbackState()
    {
        if (fbSend == null || fbText == null) return;
        fbSend.IsEnabled = !fbSending && fbText.Text.Trim().Length >= Feedback.MinText;
    }

    // «Что уйдёт вместе с сообщением» — человеку это видно до отправки
    void UpdateFeedbackSystem()
    {
        if (fbSysInfo == null) return;
        fbSysInfo.Text = fbSystem?.IsChecked == true ? "Уйдёт: " + Feedback.SystemInfo() : "";
    }

    void FeedbackSetResult(string text, bool ok)
    {
        if (fbResult == null) return;
        fbResult.Text = text;
        fbResult.Foreground = Res(ok ? "Green" : "Red");
    }

    void FeedbackAddFile()
    {
        if (fbShots.Count >= Feedback.MaxShots) { FeedbackSetResult("Можно приложить не больше трёх скриншотов.", false); return; }
        var dlg = new OpenFileDialog { Title = "Скриншот", Filter = "Картинки (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp", Multiselect = true };
        if (dlg.ShowDialog(this) != true) return;
        foreach (var file in dlg.FileNames)
        {
            if (fbShots.Count >= Feedback.MaxShots) break;
            try { FeedbackAddImage(Feedback.LoadImage(file)); }
            catch (Exception ex) { FeedbackSetResult("Не получилось добавить файл: " + ex.Message, false); }
        }
    }

    async Task FeedbackCaptureScreen()
    {
        if (fbShots.Count >= Feedback.MaxShots) { FeedbackSetResult("Можно приложить не больше трёх скриншотов.", false); return; }
        var prev = WindowState;
        WindowState = WindowState.Minimized;
        try
        {
            await Task.Delay(800);
            var bmp = await Task.Run(() => ScreenGrab.Capture());
            WindowState = prev;
            Activate();
            FeedbackAddImage(bmp);
        }
        catch (Exception ex)
        {
            WindowState = prev;
            FeedbackSetResult("Не получилось снять экран: " + ex.Message, false);
        }
    }

    void FeedbackAddImage(BitmapSource img)
    {
        var url = Feedback.ToJpegDataUrl(img);
        if (url == null) { FeedbackSetResult("Эта картинка слишком большая даже после сжатия.", false); return; }
        fbShots.Add((url, Feedback.Thumb(img)));
        RenderFeedbackShots();
        FeedbackSetResult("", true);
    }

    void RenderFeedbackShots()
    {
        if (fbShotsBox == null) return;
        fbShotsBox.Children.Clear();
        foreach (var shot in fbShots.ToList())
        {
            var holder = new Grid { Margin = new Thickness(0, 0, 10, 10) };
            holder.Children.Add(new Border
            {
                BorderBrush = Res("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Background = Res("Panel2"),
                Child = new Image { Source = shot.Thumb, Width = 150, Height = 96, Stretch = Stretch.UniformToFill, Margin = new Thickness(1) },
            });
            var x = new Button { Content = "×", Width = 24, Height = 24, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 4, 0), ToolTip = "Убрать" };
            x.Click += (_, _) => { fbShots.Remove(shot); RenderFeedbackShots(); };
            holder.Children.Add(x);
            fbShotsBox.Children.Add(holder);
        }
    }

    async Task FeedbackSend()
    {
        if (fbSending || fbText == null) return;
        var wait = Feedback.MinInterval - (DateTime.UtcNow - fbLastSent);
        if (wait > TimeSpan.Zero) { FeedbackSetResult($"Подождите {Math.Ceiling(wait.TotalSeconds)} с и отправьте снова.", false); return; }
        fbSending = true;
        UpdateFeedbackState();
        FeedbackSetResult("Отправляю…", true);
        try
        {
            var account = fbAccount?.Visibility == Visibility.Visible && fbAccount.IsChecked == true ? store.Active : null;
            await Feedback.Send(fbType, fbText.Text, fbContact?.Text, account, fbSystem?.IsChecked == true, fbShots.Select(s => s.Url).ToList());
            fbLastSent = DateTime.UtcNow;
            fbText.Text = "";
            fbShots.Clear();
            RenderFeedbackShots();
            FeedbackSetResult("Отправлено — спасибо! Ответ появится ниже, в «Ваших сообщениях».", true);
            Toast("Сообщение отправлено");
        }
        catch (Exception ex)
        {
            FeedbackSetResult("Не получилось отправить: " + BrowserInstaller.Explain(ex) + ". Текст сохранён — можно нажать «Отправить» ещё раз.", false);
        }
        finally
        {
            fbSending = false;
            UpdateFeedbackState();
            _ = RefreshFeedbackSent();
        }
    }

    // Отправленные сообщения: статус и ответ владельца подтягиваются из базы (читаются по точному номеру)
    async Task RefreshFeedbackSent()
    {
        if (fbSentBox == null) return;
        var list = Feedback.LoadSent();
        if (App.ScreenshotDir != null) return;
        fbSentBox.Children.Clear();
        if (list.Count == 0)
        {
            fbSentBox.Children.Add(new TextBlock { Text = "Пока вы ничего не отправляли.", Foreground = Res("Muted") });
            return;
        }
        var states = await Task.WhenAll(list.Take(15).Select(async item =>
        {
            try { return (FeedbackState?)await Feedback.GetState(item.Id); } catch { return null; }
        }));
        fbSentBox.Children.Clear();
        for (var i = 0; i < states.Length; i++) fbSentBox.Children.Add(FeedbackSentCard(list[i], states[i]));
    }

    UIElement FeedbackSentCard(FeedbackSent item, FeedbackState? state)
    {
        var st = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new TextBlock { Text = Feedback.TypeLabel(item.Type), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        head.Children.Add(Badge(state == null ? "Статус неизвестен" : Feedback.StatusLabel(state), state?.Status == "done" ? Res("Green") : state?.Status == "rejected" ? Res("Red") : Res("Muted"), null));
        head.Children.Add(new TextBlock { Text = DateTimeOffset.FromUnixTimeMilliseconds(item.At).ToLocalTime().ToString("dd.MM.yyyy HH:mm"), Foreground = Res("Muted"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        st.Children.Add(head);
        st.Children.Add(new TextBlock { Text = item.Preview, Foreground = Res("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        if (!string.IsNullOrWhiteSpace(state?.Reply))
        {
            var reply = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            reply.Children.Add(new TextBlock { Text = "Ответ", Foreground = Res("Accent"), FontWeight = FontWeights.SemiBold });
            reply.Children.Add(new TextBlock { Text = state!.Reply, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            st.Children.Add(new Border { Background = Res("Panel2"), CornerRadius = new CornerRadius(3), Padding = new Thickness(12, 8, 12, 10), Margin = new Thickness(0, 8, 0, 0), Child = reply });
        }
        var card = FbCard(st);
        card.Padding = new Thickness(16);
        card.Margin = new Thickness(0, 0, 0, 10);
        return card;
    }
}
