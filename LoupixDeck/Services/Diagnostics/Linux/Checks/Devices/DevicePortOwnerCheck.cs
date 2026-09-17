using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;

namespace LoupixDeck.Services.Diagnostics.Linux.Checks.Devices;

/// <summary>
/// Answers the question behind "the deck does nothing and nothing in the log says why": who
/// holds its serial port? A CDC-ACM node can be opened more than once, so a second program on
/// the port does not fail loudly - it just eats the traffic.
///
/// The owners are read from /proc, which only shows this user's processes. When something was
/// unreadable the check says so instead of claiming the port is free.
/// </summary>
internal sealed class DevicePortOwnerCheck(LinuxDeckDevice device) : ILinuxDiagnosticCheck
{
    public string Id => $"device.port-owner:{device.InstanceKey}";

    public DiagnosticCategory Category => DiagnosticCategory.DeviceAccess;

    public Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string title = DeviceCheckTitle.For(Id, device);
        int self = Environment.ProcessId;

        IReadOnlyList<(int Pid, string Name)> holders =
            LinuxDeckDeviceFacts.Holders(device.DevNode, out bool restricted);

        List<(int Pid, string Name)> others = holders.Where(holder => holder.Pid != self).ToList();

        Dictionary<string, string> evidence = new(StringComparer.Ordinal)
        {
            ["dev_node"] = device.DevNode,
            ["holders"] = holders.Count.ToString()
        };

        if (others.Count > 0)
        {
            evidence["foreign_holder"] = others[0].Name;

            DiagnosticFix fix = new(FixKind.Manual, Loc.Tr("Diagnostics_FixClosePortOwnerFmt", others[0].Name));

            return Task.FromResult(DiagnosticCheckResult.Fail(Id, Category, title,
                Loc.Tr("Diagnostics_DevicePortTakenFmt", others[0].Name, others[0].Pid), Detail(others),
                fix, evidence, others[0].Name));
        }

        if (holders.Count > 0)
        {
            return Task.FromResult(DiagnosticCheckResult.Pass(Id, Category, title,
                Loc.Tr("Diagnostics_DevicePortOwnedBySelf"), Detail(holders), evidence,
                Loc.Tr("Diagnostics_ValueThisApp")));
        }

        if (restricted)
        {
            // Another user's process would be invisible here, so "nobody holds it" is not a
            // statement this check is allowed to make.
            return Task.FromResult(DiagnosticCheckResult.Unknown(Id, Category, title,
                Loc.Tr("Diagnostics_DevicePortOwnerRestricted"), null, Loc.Tr("Diagnostics_ValueUnknown")));
        }

        return Task.FromResult(DiagnosticCheckResult.Pass(Id, Category, title,
            Loc.Tr("Diagnostics_DevicePortFree"), null, evidence, Loc.Tr("Diagnostics_ValueFree")));
    }

    private static string Detail(IEnumerable<(int Pid, string Name)> holders)
        => string.Join("\n", holders.Select(holder => $"pid {holder.Pid} ({holder.Name})"));
}
