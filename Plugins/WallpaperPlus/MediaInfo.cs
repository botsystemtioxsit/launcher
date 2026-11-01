using Windows.Media.Control;

namespace WallpaperPlusApp;

// Что сейчас играет (название, исполнитель, позиция): системный диспетчер медиа Windows — тот же источник, что у плавающей панели громкости.
sealed class MediaInfo
{
    public sealed class Snap
    {
        public bool Has; public string Title = "", Artist = "", Album = "", App = "";
        public bool Playing; public double Pos, Dur; public long TickAt;
    }

    GlobalSystemMediaTransportControlsSessionManager? mgr;
    bool busy;
    public Snap Last { get; private set; } = new();

    // Опрос в фоне; результат — в Last (обновляется раз в секунду из такта)
    public async Task PollAsync()
    {
        if (busy) return;
        busy = true;
        try
        {
            mgr ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var s = mgr.GetCurrentSession();
            if (s == null) { Last = new Snap(); return; }
            var props = await s.TryGetMediaPropertiesAsync();
            var tl = s.GetTimelineProperties();
            var pb = s.GetPlaybackInfo();
            var playing = pb.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var pos = tl.Position.TotalSeconds;
            if (playing) pos += Math.Max(0, (DateTimeOffset.Now - tl.LastUpdatedTime).TotalSeconds);
            var dur = Math.Max(0, tl.EndTime.TotalSeconds - tl.StartTime.TotalSeconds);
            if (dur > 0) pos = Math.Min(pos, dur);
            var app = s.SourceAppUserModelId ?? "";
            var cut = app.IndexOfAny(new[] { '!', '.' });
            Last = new Snap
            {
                Has = !string.IsNullOrWhiteSpace(props.Title) || !string.IsNullOrWhiteSpace(props.Artist),
                Title = props.Title ?? "", Artist = props.Artist ?? "", Album = props.AlbumTitle ?? "",
                App = cut > 0 ? app[..cut] : app, Playing = playing, Pos = pos, Dur = dur, TickAt = Environment.TickCount64,
            };
        }
        catch (Exception ex)
        {
            Paths.Log("медиа: " + ex.Message);
            Last = new Snap();
            mgr = null;
        }
        finally { busy = false; }
    }

    public object Message()
    {
        var s = Last;
        var pos = s.Pos + (s.Playing ? (Environment.TickCount64 - s.TickAt) / 1000.0 : 0);
        if (s.Dur > 0) pos = Math.Min(pos, s.Dur);
        return new { has = s.Has, title = s.Title, artist = s.Artist, album = s.Album, app = s.App, playing = s.Playing, pos = Math.Round(pos, 1), dur = Math.Round(s.Dur, 1) };
    }
}
