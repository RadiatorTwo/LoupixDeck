using System.Diagnostics;

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