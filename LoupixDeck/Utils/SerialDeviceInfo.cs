using System.Diagnostics;
using LoupixDeck.Utils;

#if WINDOWS
using System.Diagnostics.CodeAnalysis;
using System.Management;
using System.Text.RegularExpressions;
#endif

public static partial class SerialDeviceHelper
{
    // NormalizedSerial is the platform-uniform identity value (Windows hex→ASCII
    // decoded, '&'-synthesized location ids → null); Serial keeps the raw value
    // for debugging. See LoupixDeck.Utils.SerialNormalizer.
    public record SerialDeviceInfo(
        string DevNode,
        string Vid,
        string Pid,
        string Serial,
        string NormalizedSerial,
        string Manufacturer,
        string Product,
        string[] Aliases
    );

#if WINDOWS
    [GeneratedRegex(@"PID_([0-9A-F]{4})", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex regexPid { get; }

    [GeneratedRegex(@"VID_([0-9A-F]{4})", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex regexVid { get; }

    [GeneratedRegex(@"\(COM(\d+)\)", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex regexCom { get; }

    // SuppressMessage rather than [SupportedOSPlatform] — the latter cascades to
    // every caller and forces platform attributes on otherwise cross-platform
    // code (the Linux #else branch implements the same API). The WMI call is
    // only reachable when the WINDOWS constant is defined, so the analyzer
    // warning is informational, not load-bearing.
    [SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
    public static List<SerialDeviceInfo> ListSerialUsbDevices()
    {
        var result = new List<SerialDeviceInfo>();

        using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");

        foreach (var device in searcher.Get().OfType<ManagementObject>())
        {
            var name = device["Name"]?.ToString(); // z.B. "USB Serial Device (COM3)"
            var deviceId = device["PNPDeviceID"]?.ToString(); // z.B. "USB\\VID_2341&PID_0043\\..."

            if (string.IsNullOrEmpty(deviceId) || string.IsNullOrEmpty(name)) continue;

            var matchVid = regexVid.Match(deviceId);
            var matchPid = regexPid.Match(deviceId);
            var matchCom = regexCom.Match(name);

            var vid = matchVid.Success ? matchVid.Groups[1].Value : null;
            var pid = matchPid.Success ? matchPid.Groups[1].Value : null;
            var comPort = matchCom.Success ? $"COM{matchCom.Groups[1].Value}" : null;

            var parts = deviceId.Split('\\');
            var serial = parts.Length > 2 ? parts[2] : null;

            var manufacturer = device["Manufacturer"]?.ToString();
            var product = name;

            if (!string.IsNullOrEmpty(comPort))
            {
                result.Add(new SerialDeviceInfo(
                    DevNode: comPort,
                    Vid: vid,
                    Pid: pid,
                    Serial: serial,
                    NormalizedSerial: SerialNormalizer.NormalizeWindowsPnpSegment(serial),
                    Manufacturer: manufacturer,
                    Product: product,
                    Aliases: null
                ));
            }
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

            var serialShort = Get("ID_SERIAL_SHORT");

            result.Add(new SerialDeviceInfo(
                DevNode: dev,
                Vid: Get("ID_VENDOR_ID"),
                Pid: Get("ID_MODEL_ID"),
                Serial: serialShort,
                // Linux udev already yields decoded ASCII — use it as-is, but route
                // empty/whitespace to null so it matches the Windows null fallback.
                NormalizedSerial: string.IsNullOrWhiteSpace(serialShort) ? null : serialShort,
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