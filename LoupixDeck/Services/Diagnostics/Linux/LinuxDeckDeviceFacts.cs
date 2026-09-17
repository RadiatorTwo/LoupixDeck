using LoupixDeck.Registry;
using LoupixDeck.Utils;

namespace LoupixDeck.Services.Diagnostics.Linux;

/// <summary>
/// One supported deck as the system currently sees it: the registry entry it matches, the
/// serial node it is reachable through, and its USB identity.
/// </summary>
/// <param name="Info">The registry entry for this VID/PID.</param>
/// <param name="DevNode">The /dev/ttyACM* node udev reports for it.</param>
/// <param name="Serial">The raw serial. Never reaches a report unshortened.</param>
internal sealed record LinuxDeckDevice(
    DeviceRegistry.DeviceInfo Info,
    string DevNode,
    string Vid,
    string Pid,
    string Serial)
{
    /// <summary>The instance part of a per-device check id. Carries no serial in clear text.</summary>
    public string InstanceKey
    {
        get
        {
            string serial = SerialNormalizer.ForFilename(Serial);

            return string.IsNullOrEmpty(serial)
                ? $"{Vid}-{Pid}-{Path.GetFileName(DevNode)}"
                : $"{Vid}-{Pid}-{serial}";
        }
    }

    /// <summary>What the user sees: the model, plus the shortened serial when there is one.</summary>
    public string Label => string.IsNullOrEmpty(Serial)
        ? Info.Name
        : $"{Info.Name} ({DiagnosticReportSanitizer.ShortenSerial(Serial)})";
}

/// <summary>
/// Finds the supported decks attached to this machine (issue #258 phase 2).
///
/// It reads what udev already knows through <see cref="SerialDeviceHelper"/> - the same source
/// the app itself resolves devices with - and never opens a node. A deck the host has open must
/// not be disturbed by a diagnostic run.
/// </summary>
internal static class LinuxDeckDeviceFacts
{
    /// <summary>Every attached serial device whose VID/PID is in the device registry.</summary>
    public static IReadOnlyList<LinuxDeckDevice> Enumerate()
    {
        List<LinuxDeckDevice> decks = [];

        foreach (SerialDeviceHelper.SerialDeviceInfo device in SerialDeviceHelper.ListSerialUsbDevices())
        {
            DeviceRegistry.DeviceInfo info = DeviceRegistry.GetDeviceByVidPid(device.Vid, device.Pid);

            if (info == null)
            {
                continue;
            }

            decks.Add(new LinuxDeckDevice(info, device.DevNode, device.Vid.ToLowerInvariant(),
                device.Pid.ToLowerInvariant(), device.NormalizedSerial));
        }

        return decks
            .OrderBy(deck => deck.Info.Name, StringComparer.Ordinal)
            .ThenBy(deck => deck.DevNode, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The processes that currently hold <paramref name="devNode"/> open, by scanning the
    /// file descriptors under /proc. Only processes of this user are visible, so an empty list
    /// does not prove the node is free - <paramref name="restricted"/> says whether anything
    /// was unreadable.
    /// </summary>
    public static IReadOnlyList<(int Pid, string Name)> Holders(string devNode, out bool restricted)
    {
        List<(int, string)> holders = [];
        restricted = false;

        foreach (string directory in SafeEnumerate("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), out int pid))
            {
                continue;
            }

            bool holds = false;

            try
            {
                foreach (string descriptor in Directory.EnumerateFileSystemEntries($"/proc/{pid}/fd"))
                {
                    if (ResolveLink(descriptor) == devNode)
                    {
                        holds = true;
                        break;
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                restricted = true;
                continue;
            }
            catch (Exception)
            {
                // The process ended while it was being read. Nothing to report about it.
                continue;
            }

            if (holds)
            {
                holders.Add((pid, ProcessName(pid)));
            }
        }

        return holders;
    }

    /// <summary>The comm value of a process, or "?" when it is gone or unreadable.</summary>
    private static string ProcessName(int pid)
    {
        try
        {
            return File.ReadAllText($"/proc/{pid}/comm").Trim();
        }
        catch (Exception)
        {
            return "?";
        }
    }

    private static string ResolveLink(string path)
    {
        try
        {
            return File.ResolveLinkTarget(path, true)?.FullName;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static IEnumerable<string> SafeEnumerate(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path);
        }
        catch (Exception)
        {
            return [];
        }
    }
}
