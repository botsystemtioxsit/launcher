using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using LibreHardwareMonitor.Hardware;

namespace PcStats;

internal static class Program
{
    private const int Port = 47821;

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.Title = "Lock Screen PC Stats";
        Console.WriteLine("Lock Screen PC Stats — данные компьютера на экран телефона");
        Console.WriteLine("Телефон — кабелем. Либо включите на нём «USB-модем», либо «Отладку по USB» (Параметры разработчика).");
        Console.WriteLine("Закрыть программу — крестик окна или Ctrl+C.");
        Console.WriteLine();

        IPAddress? fixedTarget = null;
        if (args.Length > 0 && IPAddress.TryParse(args[0], out var ip))
        {
            fixedTarget = ip;
            Console.WriteLine("Адрес телефона задан вручную: " + ip);
        }

        var pc = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true, IsMotherboardEnabled = true, IsStorageEnabled = true };
        try
        {
            pc.Open();
        }
        catch (Exception e)
        {
            Console.WriteLine("Не удалось открыть датчики: " + e.Message);
            Console.WriteLine("Запустите программу от имени администратора.");
            Console.ReadKey();
            return 1;
        }

        var fpsMeter = new FpsMeter();
        if (!fpsMeter.Start() && fpsMeter.Status.Length > 0)
        {
            Console.WriteLine(fpsMeter.Status);
        }
        var net = new NetMeter();
        using var udp = new UdpClient();
        string host = Sanitize(Environment.MachineName, 40);
        bool waitingShown = false;
        string lastMode = "";
        var adb = new Adb();
        if (!args.Contains("--no-update"))
        {
            Task.Run(() => Updater.RunOnce(adb));
        }

        while (true)
        {
            try
            {
                var s = Read(pc);
                s.Net = net.Sample();
                var f = fpsMeter.Snapshot();
                if (f != null)
                {
                    s.Fps = f.Value.Fps;
                    s.Fps1 = f.Value.Low;
                    s.Ft = f.Value.Ft;
                    s.Game = f.Value.Game;
                }
                byte[] b = Encoding.UTF8.GetBytes(ToJson(host, s));
                var target = fixedTarget ?? FindPhone();
                string mode;
                if (target != null)
                {
                    udp.Send(b, b.Length, new IPEndPoint(target, Port));
                    mode = "USB-модем, " + target;
                }
                else if (adb.TrySend(b))
                {
                    mode = "отладка по USB";
                }
                else
                {
                    if (!waitingShown)
                    {
                        Console.WriteLine(adb.Status);
                        waitingShown = true;
                    }
                    lastMode = "";
                    Thread.Sleep(2000);
                    continue;
                }
                waitingShown = false;
                if (mode != lastMode)
                {
                    Console.WriteLine();
                    Console.WriteLine("Телефон подключён: " + mode);
                    lastMode = mode;
                }
                string line = $"ЦП {Fmt(s.CpuT)} °C / {Fmt(s.CpuL)} %, видеокарта {Fmt(s.GpuT)} °C, память {Fmt(s.RamU)}/{Fmt(s.RamT)} ГБ";
                Console.Write("\r" + line.PadRight(Math.Max(Console.WindowWidth - 1, line.Length)));
            }
            catch (SocketException)
            {
                lastMode = "";
            }
            catch (Exception e)
            {
                Console.WriteLine();
                Console.WriteLine("Ошибка: " + e.Message);
            }
            Thread.Sleep(1000);
        }
    }

    // Телефон в режиме «USB-модем» — это шлюз сети, созданной кабелем (у Android обычно 192.168.42.129).
    private static IPAddress? FindPhone()
    {
        IPAddress? any = null;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }
            string d = (nic.Description + " " + nic.Name).ToLowerInvariant();
            bool rndis = d.Contains("rndis") || d.Contains("remote ndis") || d.Contains("android") || d.Contains("usb") || d.Contains("samsung");
            foreach (var gw in nic.GetIPProperties().GatewayAddresses)
            {
                if (gw.Address.AddressFamily != AddressFamily.InterNetwork || gw.Address.Equals(IPAddress.Any))
                {
                    continue;
                }
                var bytes = gw.Address.GetAddressBytes();
                bool tether = bytes[0] == 192 && bytes[1] == 168 && bytes[2] == 42;
                if (rndis || tether)
                {
                    return gw.Address;
                }
            }
        }
        return any;
    }

    private sealed class Snap
    {
        public string CpuName = "", GpuName = "";
        public double? CpuT, CpuL, GpuT, GpuL, RamU, RamT, CpuW;
        public double? CpuClk, GpuW, GpuClk, VramU, VramT, Fan, DiskT, DiskU, Fps, Fps1, Ft;
        public (double Down, double Up)? Net;
        public string Game = "";
    }

    private static Snap Read(Computer pc)
    {
        var s = new Snap();
        double? coreMax = null, coreAvg = null, pkg = null;
        double clkSum = 0;
        int clkN = 0;
        double? fan = null;
        foreach (var hw in pc.Hardware)
        {
            hw.Update();
            foreach (var sub in hw.SubHardware)
            {
                sub.Update();
            }
            switch (hw.HardwareType)
            {
                case HardwareType.Cpu:
                    s.CpuName = Sanitize(hw.Name, 60);
                    foreach (var x in hw.Sensors)
                    {
                        if (x.Value is not float v || float.IsNaN(v)) continue;
                        string n = x.Name;
                        if (x.SensorType == SensorType.Temperature)
                        {
                            if (n.Contains("Package", StringComparison.OrdinalIgnoreCase) || n.Contains("Tctl", StringComparison.OrdinalIgnoreCase) || n.Contains("Tdie", StringComparison.OrdinalIgnoreCase)) pkg ??= v;
                            else if (n.Contains("Max", StringComparison.OrdinalIgnoreCase)) coreMax ??= v;
                            else if (n.Contains("Average", StringComparison.OrdinalIgnoreCase)) coreAvg ??= v;
                            else if (n.StartsWith("Core", StringComparison.OrdinalIgnoreCase)) coreMax = Math.Max(coreMax ?? v, v);
                        }
                        else if (x.SensorType == SensorType.Load && n.Equals("CPU Total", StringComparison.OrdinalIgnoreCase))
                        {
                            s.CpuL = v;
                        }
                        else if (x.SensorType == SensorType.Power && n.Contains("Package", StringComparison.OrdinalIgnoreCase))
                        {
                            s.CpuW = v;
                        }
                        else if (x.SensorType == SensorType.Clock && n.StartsWith("Core #", StringComparison.OrdinalIgnoreCase) && v > 100)
                        {
                            clkSum += v;
                            clkN++;
                        }
                    }
                    break;
                case HardwareType.GpuNvidia:
                case HardwareType.GpuAmd:
                case HardwareType.GpuIntel:
                    if (s.GpuName.Length > 0 && hw.HardwareType == HardwareType.GpuIntel) break; // встроенную видеокарту не показываем, если есть дискретная
                    s.GpuName = Sanitize(hw.Name, 60);
                    s.GpuT = null;
                    s.GpuL = null;
                    foreach (var x in hw.Sensors)
                    {
                        if (x.Value is not float v || float.IsNaN(v)) continue;
                        if (x.SensorType == SensorType.Temperature && s.GpuT == null && x.Name.Contains("Core", StringComparison.OrdinalIgnoreCase)) s.GpuT = v;
                        else if (x.SensorType == SensorType.Temperature && s.GpuT == null) s.GpuT = v;
                        else if (x.SensorType == SensorType.Load && x.Name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase)) s.GpuL = v;
                        else if (x.SensorType == SensorType.Power && s.GpuW == null) s.GpuW = v;
                        else if (x.SensorType == SensorType.Clock && x.Name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase)) s.GpuClk = v;
                        else if (x.SensorType == SensorType.SmallData && x.Name.Equals("GPU Memory Used", StringComparison.OrdinalIgnoreCase)) s.VramU = v / 1024.0;
                        else if (x.SensorType == SensorType.SmallData && x.Name.Equals("GPU Memory Total", StringComparison.OrdinalIgnoreCase)) s.VramT = v / 1024.0;
                        else if (x.SensorType == SensorType.Fan && fan == null && v > 0) fan = v;
                    }
                    break;
                case HardwareType.Storage:
                    if (s.DiskT == null)
                    {
                        foreach (var x in hw.Sensors)
                        {
                            if (x.SensorType == SensorType.Temperature && x.Value is float v && !float.IsNaN(v))
                            {
                                s.DiskT = v;
                                break;
                            }
                        }
                    }
                    break;
                case HardwareType.Motherboard:
                    foreach (var sub in hw.SubHardware)
                    {
                        foreach (var x in sub.Sensors)
                        {
                            if (x.SensorType == SensorType.Fan && fan == null && x.Value is float v && !float.IsNaN(v) && v > 0) fan = v;
                        }
                    }
                    break;
                case HardwareType.Memory:
                    double? used = null, avail = null;
                    foreach (var x in hw.Sensors)
                    {
                        if (x.Value is not float v || float.IsNaN(v) || x.SensorType != SensorType.Data) continue;
                        if (x.Name.Equals("Memory Used", StringComparison.OrdinalIgnoreCase)) used = v;
                        else if (x.Name.Equals("Memory Available", StringComparison.OrdinalIgnoreCase)) avail = v;
                    }
                    if (used != null && avail != null)
                    {
                        s.RamU = used;
                        s.RamT = used + avail;
                    }
                    break;
            }
        }
        s.CpuT = pkg ?? coreMax ?? coreAvg;
        if (clkN > 0) s.CpuClk = clkSum / clkN;
        s.Fan = fan;
        try
        {
            var d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            s.DiskU = 100.0 * (d.TotalSize - d.TotalFreeSpace) / d.TotalSize;
        }
        catch
        {
        }
        return s;
    }

    private static string ToJson(string host, Snap s)
    {
        var sb = new StringBuilder("{\"v\":1");
        sb.Append(",\"host\":\"").Append(Esc(host)).Append('"');
        sb.Append(",\"cpuName\":\"").Append(Esc(s.CpuName)).Append('"');
        sb.Append(",\"gpuName\":\"").Append(Esc(s.GpuName)).Append('"');
        Num(sb, "cpuT", s.CpuT); Num(sb, "cpuL", s.CpuL); Num(sb, "cpuW", s.CpuW);
        Num(sb, "gpuT", s.GpuT); Num(sb, "gpuL", s.GpuL);
        Num(sb, "ramU", s.RamU); Num(sb, "ramT", s.RamT);
        Num(sb, "cpuClk", s.CpuClk); Num(sb, "gpuW", s.GpuW); Num(sb, "gpuClk", s.GpuClk);
        Num(sb, "vramU", s.VramU); Num(sb, "vramT", s.VramT); Num(sb, "fan", s.Fan);
        Num(sb, "diskT", s.DiskT); Num(sb, "diskU", s.DiskU);
        if (s.Net != null) { Num(sb, "netD", s.Net.Value.Down); Num(sb, "netU", s.Net.Value.Up); }
        Num(sb, "fps", s.Fps); Num(sb, "fps1", s.Fps1); Num(sb, "ft", s.Ft);
        if (s.Game.Length > 0) sb.Append(",\"game\":\"").Append(Esc(Sanitize(s.Game, 40))).Append('"');
        long up = Environment.TickCount64 / 60000;
        sb.Append(",\"uptime\":\"").Append(up / 60).Append(" ч ").Append(up % 60).Append(" мин\"");
        return sb.Append('}').ToString();
    }

    private static void Num(StringBuilder sb, string k, double? v)
    {
        if (v == null || double.IsNaN(v.Value)) return;
        sb.Append(",\"").Append(k).Append("\":").Append(v.Value.ToString("0.#", CultureInfo.InvariantCulture));
    }

    private sealed class NetMeter
    {
        private long lastIn, lastOut;
        private DateTime lastAt = DateTime.MinValue;

        public (double Down, double Up)? Sample()
        {
            long i = 0, o = 0;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback || nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                try
                {
                    var st = nic.GetIPv4Statistics();
                    i += st.BytesReceived;
                    o += st.BytesSent;
                }
                catch
                {
                }
            }
            var now = DateTime.UtcNow;
            (double, double)? r = null;
            if (lastAt != DateTime.MinValue && i >= lastIn && o >= lastOut)
            {
                double sec = Math.Max(0.2, (now - lastAt).TotalSeconds);
                r = ((i - lastIn) * 8 / 1e6 / sec, (o - lastOut) * 8 / 1e6 / sec);
            }
            lastIn = i;
            lastOut = o;
            lastAt = now;
            return r;
        }
    }

    private static string Fmt(double? v) => v == null ? "—" : v.Value.ToString("0.#", CultureInfo.InvariantCulture);

    private static string Esc(string s) => s.Replace("\\", "").Replace("\"", "");

    private static string Sanitize(string s, int max)
    {
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            if (sb.Length >= max) break;
            sb.Append(c < 32 || c == 127 ? ' ' : c);
        }
        return sb.ToString().Trim();
    }
}
