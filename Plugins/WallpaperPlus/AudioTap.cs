using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace WallpaperPlusApp;

// Уровень и спектр звука, который сейчас играет в системе (петля захвата WASAPI). Звук не записывается и никуда не отправляется:
// из каждого блока считаются только 32 числа «высоты полос», остальное сразу забывается.
sealed class AudioTap : IDisposable
{
    const int Fft = 1024, Bands = 32;
    readonly object gate = new();
    readonly float[] ring = new float[Fft];
    int wpos;
    long lastDataTick;
    WasapiLoopbackCapture? cap;
    MMDeviceEnumerator? enumerator;
    MMDevice? device;
    string deviceId = "";
    long nextCheck;
    int sampleRate = 48000;
    readonly float[] bands = new float[Bands];
    float level;
    float vol; bool muted; long nextVol;
    public bool Active { get; private set; }
    public float Vol => vol;

    void Start()
    {
        try
        {
            enumerator ??= new MMDeviceEnumerator();
            device?.Dispose();
            device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            deviceId = device.ID;
            cap = new WasapiLoopbackCapture(device);
            sampleRate = cap.WaveFormat.SampleRate;
            cap.DataAvailable += OnData;
            cap.RecordingStopped += (_, _) => Active = false;
            cap.StartRecording();
            Active = true;
        }
        catch (Exception ex)
        {
            Paths.Log("звук: " + ex.Message);
            StopCapture();
        }
    }

    void StopCapture()
    {
        Active = false;
        try { cap?.StopRecording(); } catch { }
        try { cap?.Dispose(); } catch { }
        cap = null;
    }

    void OnData(object? sender, WaveInEventArgs e)
    {
        try
        {
            var fmt = ((WasapiLoopbackCapture)sender!).WaveFormat;
            var ch = Math.Max(1, fmt.Channels);
            var bytes = e.BytesRecorded;
            lock (gate)
            {
                lastDataTick = Environment.TickCount64;
                if (fmt.BitsPerSample == 32)
                {
                    var frames = bytes / (4 * ch);
                    for (var i = 0; i < frames; i++)
                    {
                        float sum = 0;
                        for (var c = 0; c < ch; c++) sum += BitConverter.ToSingle(e.Buffer, (i * ch + c) * 4);
                        ring[wpos] = sum / ch; wpos = (wpos + 1) % Fft;
                    }
                }
                else if (fmt.BitsPerSample == 16)
                {
                    var frames = bytes / (2 * ch);
                    for (var i = 0; i < frames; i++)
                    {
                        float sum = 0;
                        for (var c = 0; c < ch; c++) sum += BitConverter.ToInt16(e.Buffer, (i * ch + c) * 2) / 32768f;
                        ring[wpos] = sum / ch; wpos = (wpos + 1) % Fft;
                    }
                }
            }
        }
        catch { }
    }

    // Снимок для страницы: вызывается ~25 раз в секунду
    public object Snapshot()
    {
        var now = Environment.TickCount64;
        if (cap == null && now >= nextCheck) { nextCheck = now + 5000; Start(); }
        else if (cap != null && now >= nextCheck)
        {
            nextCheck = now + 5000;
            try { if (enumerator != null && enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID != deviceId) { StopCapture(); Start(); } } catch { }
        }
        if (device != null && now >= nextVol)
        {
            nextVol = now + 250;
            try { vol = device.AudioEndpointVolume.MasterVolumeLevelScalar; muted = device.AudioEndpointVolume.Mute; } catch { }
        }

        float[] frame = new float[Fft];
        bool fresh;
        lock (gate)
        {
            fresh = now - lastDataTick < 150;
            for (var i = 0; i < Fft; i++) frame[i] = ring[(wpos + i) % Fft];
        }
        if (!fresh)
        {
            // тишина: полосы плавно опускаются
            for (var i = 0; i < Bands; i++) bands[i] *= 0.82f;
            level *= 0.82f;
        }
        else
        {
            double rms = 0;
            for (var i = 0; i < Fft; i++) rms += frame[i] * frame[i];
            rms = Math.Sqrt(rms / Fft);
            level = Math.Max((float)Math.Min(1, rms * 4), level * 0.85f);
            var re = new double[Fft]; var im = new double[Fft];
            for (var i = 0; i < Fft; i++) re[i] = frame[i] * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (Fft - 1)));
            Transform(re, im);
            var binHz = (double)sampleRate / Fft;
            var maxHz = Math.Min(16000, sampleRate / 2.0);
            for (var b = 0; b < Bands; b++)
            {
                var f0 = 40 * Math.Pow(maxHz / 40, (double)b / Bands);
                var f1 = 40 * Math.Pow(maxHz / 40, (double)(b + 1) / Bands);
                var i0 = Math.Max(1, (int)Math.Floor(f0 / binHz));
                var i1 = Math.Max(i0 + 1, (int)Math.Ceiling(f1 / binHz));
                double m = 0;
                for (var i = i0; i < Math.Min(i1, Fft / 2); i++) m = Math.Max(m, Math.Sqrt(re[i] * re[i] + im[i] * im[i]));
                var db = 20 * Math.Log10(m / (Fft / 4.0) + 1e-9);
                var v = (float)Math.Clamp((db + 62) / 52, 0, 1);
                bands[b] = Math.Max(v, bands[b] * 0.8f);
            }
        }
        var outBands = new double[Bands];
        for (var i = 0; i < Bands; i++) outBands[i] = Math.Round(bands[i], 3);
        return new { level = Math.Round(level, 3), bands = outBands, vol = Math.Round(vol, 3), muted, active = Active };
    }

    // Быстрое преобразование Фурье (по основанию 2) на месте
    static void Transform(double[] re, double[] im)
    {
        var n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (var len = 2; len <= n; len <<= 1)
        {
            var ang = -2 * Math.PI / len;
            double wr = Math.Cos(ang), wi = Math.Sin(ang);
            for (var i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (var k = 0; k < len / 2; k++)
                {
                    var ur = re[i + k]; var ui = im[i + k];
                    var vr = re[i + k + len / 2] * cr - im[i + k + len / 2] * ci;
                    var vi = re[i + k + len / 2] * ci + im[i + k + len / 2] * cr;
                    re[i + k] = ur + vr; im[i + k] = ui + vi;
                    re[i + k + len / 2] = ur - vr; im[i + k + len / 2] = ui - vi;
                    var t = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = t;
                }
            }
        }
    }

    public void Dispose()
    {
        StopCapture();
        try { device?.Dispose(); } catch { }
        device = null;
        try { enumerator?.Dispose(); } catch { }
        enumerator = null;
    }
}
