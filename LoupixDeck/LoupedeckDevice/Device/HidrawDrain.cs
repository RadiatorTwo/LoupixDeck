namespace LoupixDeck.LoupedeckDevice.Device;

/// <summary>
/// Linux only: keeps reading — and discarding — the HID reports of the USB device behind a
/// serial port, so its firmware never stalls on a report nobody picks up.
///
/// The Razer Stream Controller X queues every key as a HID report on a vendor-defined HID
/// interface before it sends the serial BUTTON_PRESS frame. Linux' usbhid only polls an
/// interrupt endpoint while someone has the device open, and nothing opens this one (vendor
/// usages create no input device), so the first report stays in the device and the serial
/// frame never follows. Windows polls HID endpoints unconditionally, which is why the keys
/// work there without this. Measured on firmware 0.2.26: with the hidraw node closed no key
/// reached the serial port at all; the moment it was opened the stalled press arrived.
/// </summary>
internal sealed class HidrawDrain : IDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private readonly Func<string> _serialPath;
    private volatile bool _running = true;
    private string _lastMessage;

    /// <param name="serialPath">The device's current serial port; read again on every attempt
    /// because the port can move when the device re-enumerates.</param>
    public HidrawDrain(Func<string> serialPath)
    {
        _serialPath = serialPath;
        new Thread(Run) { IsBackground = true, Name = "HidrawDrain" }.Start();
    }

    /// <summary>
    /// Stops draining. A read in progress blocks until the device's next report, so the node
    /// is released after the next key event (or on unplug) rather than immediately.
    /// </summary>
    public void Dispose() => _running = false;

    private void Run()
    {
        byte[] buffer = new byte[64];

        while (_running)
        {
            string node = null;
            try
            {
                node = FindHidrawNode(_serialPath());
                if (node == null)
                {
                    Log("[HID] No hidraw node found for the Stream Controller X; its keys may not arrive until it has one.");
                }
                else
                {
                    using FileStream stream = new(node, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 0);
                    Log($"[HID] Draining '{node}' so the device keeps sending its keys over the serial port.");

                    while (_running && stream.Read(buffer, 0, buffer.Length) > 0)
                    {
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                Log($"[HID] Permission denied opening '{node}'. The Stream Controller X keys will not work until " +
                    "LoupixDeck can read it — add the hidraw udev rule for 1532:0d09 (see README).");
            }
            catch (Exception ex)
            {
                // Typically the device was unplugged mid-read; look the node up again.
                Log($"[HID] Draining '{node}' stopped: {ex.Message}");
            }

            if (_running)
                Thread.Sleep(RetryDelay);
        }
    }

    /// <summary>Logs a message once, until a different one replaces it — the retry loop would
    /// otherwise repeat the same line every few seconds.</summary>
    private void Log(string message)
    {
        if (message == _lastMessage) return;
        _lastMessage = message;
        Console.WriteLine(message);
    }

    /// <summary>
    /// Finds the /dev/hidraw* node that belongs to the same USB device as
    /// <paramref name="serialPath"/> — matched through sysfs topology rather than VID/PID or
    /// serial number, so two identical units never pick each other's node.
    /// </summary>
    internal static string FindHidrawNode(string serialPath)
    {
        if (string.IsNullOrEmpty(serialPath) || !File.Exists(serialPath)) return null;

        // /dev/serial/by-id/... -> /dev/ttyACM2
        string tty = Path.GetFileName(RealPath(serialPath));

        // /sys/devices/.../5-4.3/5-4.3:1.0/tty/ttyACM2 -> /sys/devices/.../5-4.3
        string usbDevice = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(
            RealPath($"/sys/class/tty/{tty}"))));
        if (string.IsNullOrEmpty(usbDevice) || !Directory.Exists("/sys/class/hidraw")) return null;

        // /sys/devices/.../5-4.3/5-4.3:1.6/0003:1532:0D09.000F/hidraw/hidraw14
        foreach (string entry in Directory.EnumerateFileSystemEntries("/sys/class/hidraw"))
        {
            if (RealPath(entry).StartsWith(usbDevice + "/", StringComparison.Ordinal))
                return $"/dev/{Path.GetFileName(entry)}";
        }

        return null;
    }

    private static string RealPath(string path) =>
        new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path;
}
