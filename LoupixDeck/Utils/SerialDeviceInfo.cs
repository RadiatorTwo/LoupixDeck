using System.Diagnostics;
using LoupixDeck.Utils;

#if WINDOWS
using System.Diagnostics.CodeAnalysis;
using System.Management;
#endif

public static class SerialDeviceHelper
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

            var vid = TryExtractTaggedHex4(deviceId, "VID_");
            var pid = TryExtractTaggedHex4(deviceId, "PID_");
            var comPort = TryExtractComPort(name);

            var parts = deviceId.Split('\\');
            var serial = parts.Length > 2 ? parts[2] : null;

            // A composite USB device exposes its COM port as a child interface
            // ("USB\VID_2EC2&PID_0006&MI_00\7&26036A56&0&0000"), whose instance id is a
            // port-dependent value Windows generates. The iSerial sits on the parent
            // composite device ("USB\VID_2EC2&PID_0006\LDD2201…"), so read it from there.
            if (serial != null && serial.Contains('&') && TryGetParentInstanceSerial(device) is { } parentSerial)
                serial = parentSerial;

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

    /// <summary>
    /// The instance segment of the device's parent (DEVPKEY_Device_Parent), when that parent is the
    /// USB device itself ("USB\VID_…&amp;PID_…\&lt;serial&gt;"). Null when the property is unavailable or the
    /// parent is not a plain USB device node.
    /// </summary>
    [SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
    private static string TryGetParentInstanceSerial(ManagementObject device)
    {
        try
        {
            using ManagementBaseObject input = device.GetMethodParameters("GetDeviceProperties");
            input["devicePropertyKeys"] = new[] { "DEVPKEY_Device_Parent" };
            using ManagementBaseObject output = device.InvokeMethod("GetDeviceProperties", input, null);

            if (output?["deviceProperties"] is not ManagementBaseObject[] { Length: > 0 } properties)
                return null;

            string parent = properties[0]["Data"] as string;
            foreach (ManagementBaseObject property in properties)
                property.Dispose();

            string[] parts = parent?.Split('\\');
            if (parts is not { Length: 3 } ||
                !string.Equals(parts[0], "USB", StringComparison.OrdinalIgnoreCase) ||
                parts[1].Contains("&MI_", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return parts[2];
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SerialDeviceHelper] Parent lookup failed: {ex.Message}");
            return null;
        }
    }

    // VID_xxxx / PID_xxxx (4 hex digits, case-insensitive tag).
    private static string TryExtractTaggedHex4(ReadOnlySpan<char> haystack, ReadOnlySpan<char> tag)
    {
        int index = haystack.IndexOf(tag, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return null;

        int start = index + tag.Length;
        if (start + 4 > haystack.Length)
            return null;

        ReadOnlySpan<char> hex = haystack.Slice(start, 4);
        for (int i = 0; i < hex.Length; i++)
        {
            if (!char.IsAsciiHexDigit(hex[i]))
                return null;
        }

        return hex.ToString();
    }

    // "(COMn)" / "(comn)" → "COMn".
    private static string TryExtractComPort(ReadOnlySpan<char> name)
    {
        int index = name.IndexOf("(COM", StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return null;

        int digits = index + 4;
        int end = digits;
        while (end < name.Length && char.IsAsciiDigit(name[end]))
            end++;

        if (end == digits || end >= name.Length || name[end] != ')')
            return null;

        return string.Concat("COM", name.Slice(digits, end - digits));
    }
#else
    /// <summary>
    /// Unix discovery. Linux reads sysfs through <c>udevadm</c>; macOS has neither sysfs nor
    /// udev, so it walks the IO registry instead. Both fill the same record.
    /// </summary>
    public static List<SerialDeviceInfo> ListSerialUsbDevices()
    {
        return OperatingSystem.IsMacOS() ? ListMacSerialUsbDevices() : ListLinuxSerialUsbDevices();
    }

    private static List<SerialDeviceInfo> ListLinuxSerialUsbDevices()
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

    // ──────── macOS ────────

    /// <summary>
    /// macOS serial ports are <c>/dev/cu.*</c> and carry no identity of their own: the VID/PID
    /// and serial live on an ancestor USB node in the IO registry. <c>ioreg</c> prints that
    /// registry as a tree indented two spaces per level, so this walks the tree keeping an
    /// ancestor chain and, for every node owning an <c>IOCalloutDevice</c>, fills each field
    /// from the nearest ancestor that has it. Fields are filled independently because a USB
    /// interface node may carry <c>idVendor</c> without <c>idProduct</c> — stopping at the
    /// first node with any identity would drop the product id and fail the registry lookup.
    /// </summary>
    private static List<SerialDeviceInfo> ListMacSerialUsbDevices()
    {
        var result = new List<SerialDeviceInfo>();

        string dump = RunIoreg();
        if (string.IsNullOrWhiteSpace(dump)) return result;

        var stack = new List<IoRegNode>();
        var nodes = new List<IoRegNode>();
        IoRegNode current = null;

        foreach (string line in dump.Split('\n'))
        {
            int depth = NodeDepth(line);
            if (depth >= 0)
            {
                if (depth > stack.Count) depth = stack.Count;
                stack.RemoveRange(depth, stack.Count - depth);

                current = new IoRegNode { Parent = stack.Count > 0 ? stack[^1] : null };
                stack.Add(current);
                nodes.Add(current);
                continue;
            }

            if (current == null) continue;
            if (!TrySplitProperty(line, out string key, out string value)) continue;

            switch (key)
            {
                case "idVendor": current.Vid = ToHex4(value); break;
                case "idProduct": current.Pid = ToHex4(value); break;
                case "USB Serial Number": current.Serial = value; break;
                case "USB Vendor Name": current.Manufacturer = value; break;
                case "USB Product Name": current.Product = value; break;
                case "IOCalloutDevice": current.Callout = value; break;
                case "IODialinDevice": current.Dialin = value; break;
            }
        }

        foreach (IoRegNode node in nodes)
        {
            if (string.IsNullOrEmpty(node.Callout)) continue;

            string vid = null, pid = null, serial = null, manufacturer = null, product = null;
            for (IoRegNode walk = node; walk != null; walk = walk.Parent)
            {
                vid ??= walk.Vid;
                pid ??= walk.Pid;
                serial ??= walk.Serial;
                manufacturer ??= walk.Manufacturer;
                product ??= walk.Product;
            }

            // Bluetooth and the debug console also publish callout nodes; without a USB
            // identity they can never match the registry, so drop them here.
            if (vid == null || pid == null) continue;

            result.Add(new SerialDeviceInfo(
                DevNode: node.Callout,
                Vid: vid,
                Pid: pid,
                Serial: serial,
                // ioreg already yields decoded ASCII — as on Linux, route empty/whitespace
                // to null so it matches the Windows null fallback.
                NormalizedSerial: string.IsNullOrWhiteSpace(serial) ? null : serial,
                Manufacturer: manufacturer,
                Product: product,
                // The matching /dev/tty.* dial-in node, so callers can recognise either name.
                Aliases: string.IsNullOrEmpty(node.Dialin) ? [] : [node.Dialin]
            ));
        }

        return result;
    }

    private sealed class IoRegNode
    {
        public IoRegNode Parent;
        public string Vid;
        public string Pid;
        public string Serial;
        public string Manufacturer;
        public string Product;
        public string Callout;
        public string Dialin;
    }

    /// <summary>
    /// Tree depth of an ioreg node line, or -1 when the line is not a node. Node lines are
    /// <c>"  | | +-o Name@addr &lt;class ...&gt;"</c>: only spaces and pipes before the marker,
    /// two columns per level.
    /// </summary>
    private static int NodeDepth(string line)
    {
        int marker = line.IndexOf("+-o ", StringComparison.Ordinal);
        if (marker < 0) return -1;

        for (int i = 0; i < marker; i++)
        {
            if (line[i] != ' ' && line[i] != '|') return -1;
        }

        return marker / 2;
    }

    /// <summary>
    /// Pulls <c>"key" = value</c> out of an ioreg property line. Values arrive quoted
    /// (strings) or bare (numbers); quotes are stripped either way.
    /// </summary>
    private static bool TrySplitProperty(string line, out string key, out string value)
    {
        key = null;
        value = null;

        int open = line.IndexOf('"');
        if (open < 0) return false;

        int close = line.IndexOf('"', open + 1);
        if (close < 0) return false;

        int eq = line.IndexOf('=', close + 1);
        if (eq < 0) return false;

        key = line.Substring(open + 1, close - open - 1);
        value = line[(eq + 1)..].Trim();

        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            value = value[1..^1];

        return key.Length > 0 && value.Length > 0;
    }

    /// <summary>
    /// ioreg reports idVendor/idProduct in decimal, while
    /// <see cref="LoupixDeck.Registry.DeviceRegistry"/> matches the lowercase 4-digit hex that
    /// Windows and udev produce. Converting here keeps the comparison in one form.
    /// </summary>
    private static string ToHex4(string value)
    {
        return int.TryParse(value, out int id) && id >= 0 ? id.ToString("x4") : null;
    }

    private static string RunIoreg()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ioreg",
            Arguments = "-p IOService -l -w 0",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        return proc?.StandardOutput.ReadToEnd() ?? "";
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