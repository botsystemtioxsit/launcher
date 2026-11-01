using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Wpf;

namespace WallpaperPlusApp;

// Окно обоев на одном мониторе: страница (WebView2) внутри окна, которое прикреплено к рабочему столу за значками.
sealed class HostWindow : Window
{
    public Native.Monitor Monitor { get; }
    public IntPtr Handle { get; private set; }
    public bool Paused { get; private set; }
    readonly WebView2 web = new();

    public HostWindow(Native.Monitor monitor)
    {
        Monitor = monitor;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Background = Brushes.Black;
        AllowsTransparency = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -20000; Top = -20000; Width = 640; Height = 360;   // пока не прикреплено, окно далеко за краем экрана
        Content = web;
        // окно прикрепляется сразу после создания, до первого показа
        SourceInitialized += (_, _) =>
        {
            Handle = new WindowInteropHelper(this).Handle;
            Desktop.Attach(Handle, monitor.Rect);
        };
    }

    public bool IsAttached => Handle != IntPtr.Zero && Desktop.StillAttached(Handle);

    public void Reattach()
    {
        if (Handle == IntPtr.Zero) return;
        Desktop.Attach(Handle, Monitor.Rect);
    }

    public Task LoadAsync(Wallpaper w, WpSettings s) => WallpaperView.LoadAsync(web, w, s);

    public void SetMuted(bool muted) { try { if (web.CoreWebView2 != null) web.CoreWebView2.IsMuted = muted; } catch { } }

    // Пауза: страница скрыта и усыплена — видеокарта и процессор свободны
    public async Task SetPausedAsync(bool paused)
    {
        if (paused == Paused) return;
        Paused = paused;
        try
        {
            if (paused)
            {
                web.Visibility = Visibility.Hidden;
                if (web.CoreWebView2 != null) await web.CoreWebView2.TrySuspendAsync();
            }
            else
            {
                if (web.CoreWebView2 != null) web.CoreWebView2.Resume();
                web.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex) { Paths.Log("пауза: " + ex.Message); }
    }

    public void Release()
    {
        try { web.Dispose(); } catch { }
        try { Close(); } catch { }
    }
}
