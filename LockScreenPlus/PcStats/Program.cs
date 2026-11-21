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
        Console.WriteLine("Включите на телефоне «USB-модем» (Настройки → Подключения → Точка доступа и модем).");
        Console.WriteLine("Закрыть программу — крестик окна или Ctrl+C.");
        Console.WriteLine();

        IPAddress? fixedTarget = null;
        if (args.Length > 0 && IPAddress.TryParse(args[0], out var ip))
        {
            fixedTarget = ip;
            Console.WriteLine("Адрес телефона задан вручную: " + ip);
        }

        var pc = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true };
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

        using var udp = new UdpClient();
        string host = Sanitize(Environment.MachineName, 40);
        bool waitingShown = false;
        string lastTarget = "";

        while (true)
        {
            try
            {
                var target = fixedTarget ?? FindPhone();
                if (target == null)
                {
                    if (!waitingShown)
                    {
                        Console.WriteLine("Жду телефон: подключите кабель и включите «USB-модем»…");
                        waitingShown = true;
                    }
                    Thread.Sleep(2000);
                    continue;
                }
                waitingShown = false;

                var s = Read(pc);
                string json = ToJson(host, s);
                byte[] b = Encoding.UTF8.GetBytes(json);
                udp.Send(b, b.Length, new IPEndPoint(target, Port));

                string line = $"{target}: ЦП {Fmt(s.CpuT)} °C / {Fmt(s.CpuL)} %, видеокарта {Fmt(s.GpuT)} °C, память {Fmt(s.RamU)}/{Fmt(s.RamT)} ГБ";
                if (target.ToString() != lastTarget)
                {
                    Console.WriteLine("Телефон найден: " + target);
                    lastTarget = target.ToString();
                }
                Console.Write("\r" + line.PadRight(Math.Max(Console.WindowWidth - 1, line.Length)));
            }
            catch (SocketException)
            {
                // телефон отключили или сеть переключилась — ищем заново
                lastTarget = "";
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
                any ??= null;
            }
        }
        return any;
    }

    private sealed class Snap
    {
        public string CpuName = "", GpuName = "";
        public double? CpuT, CpuL, GpuT, GpuL, RamU, RamT, CpuW;
    }

    private static Snap Read(Computer pc)
    {
        var s = new Snap();
        double? coreMax = null, coreAvg = null, pkg = null;
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
        return sb.Append('}').ToString();
    }

    private static void Num(StringBuilder sb, string k, double? v)
    {
        if (v == null || double.IsNaN(v.Value)) return;
        sb.Append(",\"").Append(k).Append("\":").Append(v.Value.ToString("0.#", CultureInfo.InvariantCulture));
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
