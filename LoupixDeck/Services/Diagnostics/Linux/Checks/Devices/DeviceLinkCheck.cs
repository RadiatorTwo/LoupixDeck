using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;

namespace LoupixDeck.Services.Diagnostics.Linux.Checks.Devices;

/// <summary>
/// Reports the link the host already has to this unit, read from the device registry rather
/// than tested: the app knows whether its serial connection is open, and a diagnostic run must
/// not open a second one to find out.
///
/// A deck that is attached but has no host is the case where the app was started before the
/// deck was plugged in, or where bringing it up failed.
/// </summary>
internal sealed class DeviceLinkCheck(LinuxDeckDevice device, IDeviceHostRegistry hosts)
    : ILinuxDiagnosticCheck
{
    public string Id => $"device.link:{device.InstanceKey}";

    public DiagnosticCategory Category => DiagnosticCategory.DeviceAccess;

    public Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string title = DeviceCheckTitle.For(Id, device);
        DeviceHost host = FindHost();

        if (host == null)
        {
            return Task.FromResult(DiagnosticCheckResult.Warning(Id, Category, title,
                Loc.Tr("Diagnostics_DeviceNoHostFmt", device.Info.Name), null,
                new DiagnosticFix(FixKind.Manual, Loc.Tr("Diagnostics_FixRestartAppForDevice")), null,
                Loc.Tr("Diagnostics_ValueNotRunning")));
        }

        Dictionary<string, string> evidence = new(StringComparer.Ordinal)
        {
            ["scope_key"] = host.Device.Slug,
            ["primary"] = host.IsPrimary ? "yes" : "no"
        };

        if (host.Controller.IsDeviceConnected)
        {
            return Task.FromResult(DiagnosticCheckResult.Pass(Id, Category, title,
                Loc.Tr("Diagnostics_DeviceLinkOpen"), null, evidence, Loc.Tr("Diagnostics_ValueConnected")));
        }

        // The host exists but its serial link is down: the port was busy at startup, or the
        // device was unplugged and has not come back.
        return Task.FromResult(DiagnosticCheckResult.Fail(Id, Category, title,
            Loc.Tr("Diagnostics_DeviceLinkDown"), null,
            new DiagnosticFix(FixKind.Manual, Loc.Tr("Diagnostics_FixReplugDevice"), null,
                RequiresReconnect: true),
            evidence, Loc.Tr("Diagnostics_ValueDisconnected")));
    }

    /// <summary>
    /// The running host for this unit: by serial when both sides have one - two identical decks
    /// must not swap results - otherwise by model.
    /// </summary>
    private DeviceHost FindHost()
    {
        if (!string.IsNullOrEmpty(device.Serial))
        {
            DeviceHost bySerial = hosts.Hosts.FirstOrDefault(host =>
                string.Equals(host.Device.Serial, device.Serial, StringComparison.OrdinalIgnoreCase));

            if (bySerial != null)
            {
                return bySerial;
            }
        }

        return hosts.Hosts.FirstOrDefault(host =>
            string.Equals(host.Device.Slug, device.Info.Slug, StringComparison.Ordinal));
    }
}
