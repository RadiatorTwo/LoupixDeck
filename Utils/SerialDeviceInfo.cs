using System.Diagnostics;

#if WINDOWS
using System.Management;
#endif

public static class SerialDeviceHelper
{
    public record SerialDeviceInfo(
        string DevNode,
        string Vid,
        string Pid,
        string Serial,
        string Manufacturer,
        string Product,
        string[] Aliases
    );

#if WINDOWS
    public static List<SerialDeviceInfo> ListSerialUsbDevices()
    {
        var result = new List<SerialDeviceInfo>();
    
        using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");
    
        foreach (var device in searcher.Get())
        {
            var name = device["Name"]?.ToString(); // z.B. "USB Serial Device (COM3)"
            var deviceId = device["PNPDeviceID"]?.ToString(); // z.B. "USB\\VID_2341&PID_0043\\..."
    
            if (string.IsNullOrEmpty(deviceId)) continue;
    
            string? Extract(string prefix) =>
                deviceId.Contains(prefix) ?
                deviceId.Split(new[] { '\\', '&' }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault(s => s.StartsWith(prefix))?.Substring(prefix.Length) : null;
    
            var vid = Extract("VID_");
            var pid = Extract("PID_");
            var serial = deviceId.Split('\\').Length > 2 ? deviceId.Split('\\')[2] : null;
            var manufacturer = device["Manufacturer"]?.ToString();
            var product = name;
    
            result.Add(new SerialDeviceInfo(
                DevNode: name ?? "Unknown",
                Vid: vid,
                Pid: pid,
                Serial: serial,
                Manufacturer: manufacturer,
                Product: product,
                Aliases: null // Windows hat kein direktes Äquivalent zu /dev/aliasen
            ));
        }
    
        return result;
    }
#else
    public static List<SerialDeviceInfo> ListSerialUsbDevices()
    {
        var result = new List<SerialDeviceInfo>();
        var candidates = Directory.EnumerateFiles("/dev")
            .Where(f => f.StartsWith("/dev/ttyACM") || f.StartsWith("/dev/ttyUSB"));

        foreach (var dev in candidates)
        {
            var info = RunUdevadm(dev);
            if (string.IsNullOrWhiteSpace(info)) continue;

            string Get(string key) =>
                info.Split('\n').FirstOrDefault(line => line.StartsWith(key + "="))?.Split('=', 2)[1];

            var aliases = Get("DEVLINKS")?.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            result.Add(new SerialDeviceInfo(
                DevNode: dev,
                Vid: Get("ID_VENDOR_ID"),
                Pid: Get("ID_MODEL_ID"),
                Serial: Get("ID_SERIAL_SHORT"),
                Manufacturer: Get("ID_VENDOR"),
                Product: Get("ID_MODEL"),
                Aliases: aliases
            ));
        }

        return result;
    }

#endif

    private static string RunUdevadm(string devPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "udevadm",
            Arguments = $"info -q property -n {devPath}",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        return proc?.StandardOutput.ReadToEnd() ?? "";
    }
}