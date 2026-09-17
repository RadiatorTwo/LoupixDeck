using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;

namespace LoupixDeck.Services.Diagnostics.Linux.Checks.Devices;

/// <summary>
/// States what was found: the model behind the VID/PID and the node it is reachable through.
/// Always a pass - the deck would not be in the list otherwise - but it is what the other
/// device checks of this unit are read against, and it carries the identity into the report.
/// </summary>
internal sealed class DeviceIdentityCheck(LinuxDeckDevice device) : ILinuxDiagnosticCheck
{
    public string Id => $"device.identity:{device.InstanceKey}";

    public DiagnosticCategory Category => DiagnosticCategory.DeviceAccess;

    public Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        Dictionary<string, string> evidence = new(StringComparer.Ordinal)
        {
            ["model"] = device.Info.Name,
            ["usb_id"] = $"{device.Vid}:{device.Pid}",
            ["dev_node"] = device.DevNode,
            ["serial"] = DiagnosticReportSanitizer.ShortenSerial(device.Serial)
        };

        return Task.FromResult(DiagnosticCheckResult.Pass(Id, Category,
            DeviceCheckTitle.For(Id, device),
            Loc.Tr("Diagnostics_DeviceDetectedFmt", device.Info.Name, $"{device.Vid}:{device.Pid}"),
            null, evidence, device.DevNode));
    }
}
