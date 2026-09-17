using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;

namespace LoupixDeck.Services.Diagnostics.Linux.Checks.Devices;

/// <summary>
/// The ground truth for reaching a deck at all: may this process read and write its serial node?
///
/// Tested with access(2), never by opening the node. The app itself may hold the port, and a
/// diagnostic run must not open a second handle on a device that is in use.
/// </summary>
internal sealed class DeviceNodeAccessCheck(LinuxDeckDevice device) : ILinuxDiagnosticCheck
{
    public string Id => $"device.access:{device.InstanceKey}";

    public DiagnosticCategory Category => DiagnosticCategory.DeviceAccess;

    public Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string title = DeviceCheckTitle.For(Id, device);

        if (!File.Exists(device.DevNode))
        {
            // udev listed the node a moment ago, so it was unplugged between enumeration and here.
            return Task.FromResult(DiagnosticCheckResult.Unknown(Id, Category, title,
                Loc.Tr("Diagnostics_DeviceNodeGoneFmt", device.DevNode), null, device.DevNode));
        }

        int errno = LinuxInputInterop.TryAccess(device.DevNode,
            LinuxInputInterop.R_OK | LinuxInputInterop.W_OK);

        Dictionary<string, string> evidence = new(StringComparer.Ordinal)
        {
            ["dev_node"] = device.DevNode,
            ["mode"] = NodeMode(device.DevNode)
        };

        if (errno == 0)
        {
            return Task.FromResult(DiagnosticCheckResult.Pass(Id, Category, title,
                Loc.Tr("Diagnostics_DeviceAccessOkFmt", device.DevNode), null, evidence,
                Loc.Tr("Diagnostics_DeviceAccessReadWrite")));
        }

        string detail = $"access(\"{device.DevNode}\", R_OK|W_OK) = -1 (errno {errno})";
        evidence["errno"] = errno.ToString();

        if ((errno == LinuxInputInterop.EACCES) || (errno == LinuxInputInterop.EPERM))
        {
            DiagnosticFix fix = new(FixKind.InstallerScript, Loc.Tr("Diagnostics_FixInstallDeviceRule"),
                "./install-loupixdeck.sh", RequiresElevation: true, RequiresReconnect: true);

            return Task.FromResult(DiagnosticCheckResult.Fail(Id, Category, title,
                Loc.Tr("Diagnostics_DeviceAccessDeniedFmt", device.DevNode), detail, fix, evidence,
                $"errno {errno}"));
        }

        return Task.FromResult(DiagnosticCheckResult.Unknown(Id, Category, title,
            Loc.Tr("Diagnostics_DeviceAccessUnknownFmt", errno), detail, $"errno {errno}"));
    }

    /// <summary>The node's permission bits as a four-digit string, or "?" when unreadable.</summary>
    private static string NodeMode(string path)
    {
        try
        {
            // Guarded rather than attributed: the whole file is Linux-only in practice, but the
            // diagnostics assembly still compiles for Windows.
            if (!OperatingSystem.IsLinux())
            {
                return "?";
            }

            return Convert.ToString((int)File.GetUnixFileMode(path), 8).PadLeft(4, '0');
        }
        catch (Exception)
        {
            return "?";
        }
    }
}
