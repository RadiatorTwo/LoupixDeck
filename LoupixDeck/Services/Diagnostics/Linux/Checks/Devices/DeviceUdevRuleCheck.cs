using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;

namespace LoupixDeck.Services.Diagnostics.Linux.Checks.Devices;

/// <summary>
/// Compares the installed udev rule against what this version's installer writes for this
/// unit's VID/PID: a "tty" line for the serial port, a "usb" line for the raw USB node, and for
/// the Stream Controller X also a "hidraw" line, because its key frames stall until the HID
/// reports are drained.
///
/// A rule that is present is not automatically a rule that is in effect - it applies from the
/// next re-plug on. So a complete rule plus a node this process cannot write is reported as
/// "present but not applied yet", which is the case a reconnect fixes.
/// </summary>
internal sealed class DeviceUdevRuleCheck(LinuxDeckDevice device) : ILinuxDiagnosticCheck
{
    private static readonly string[] RulePaths =
    [
        "/etc/udev/rules.d/99-loupixdeck.rules",
        "/usr/lib/udev/rules.d/99-loupixdeck.rules"
    ];

    public string Id => $"device.udev-rule:{device.InstanceKey}";

    public DiagnosticCategory Category => DiagnosticCategory.DeviceAccess;

    public Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string title = DeviceCheckTitle.For(Id, device);
        DiagnosticFix installer = new(FixKind.InstallerScript, Loc.Tr("Diagnostics_FixInstallDeviceRule"),
            DiagnosticInstaller.Command(), RequiresElevation: true, RequiresReconnect: true);

        string rulePath = RulePaths.FirstOrDefault(File.Exists);

        if (rulePath == null)
        {
            return Task.FromResult(DiagnosticCheckResult.Warning(Id, Category, title,
                Loc.Tr("Diagnostics_DeviceRuleMissing"), null, installer, null,
                Loc.Tr("Diagnostics_ValueMissing")));
        }

        string content;

        try
        {
            content = File.ReadAllText(rulePath);
        }
        catch (Exception ex)
        {
            return Task.FromResult(DiagnosticCheckResult.Unknown(Id, Category, title,
                Loc.Tr("Diagnostics_UdevRuleUnreadable"), $"{rulePath}: {ex.Message}"));
        }

        List<string> missing = ExpectedSubsystems()
            .Where(subsystem => !HasLine(content, subsystem))
            .ToList();

        Dictionary<string, string> evidence = new(StringComparer.Ordinal)
        {
            ["rule_file"] = rulePath,
            ["usb_id"] = $"{device.Vid}:{device.Pid}",
            ["expected"] = string.Join(", ", ExpectedSubsystems())
        };

        if (missing.Count > 0)
        {
            evidence["missing"] = string.Join(", ", missing);

            return Task.FromResult(DiagnosticCheckResult.Warning(Id, Category, title,
                Loc.Tr("Diagnostics_DeviceRuleIncompleteFmt", string.Join(", ", missing)), null,
                installer, evidence, Loc.Tr("Diagnostics_ValueIncomplete")));
        }

        // The rule covers this unit. Whether it took effect is a question about the node.
        bool writable = LinuxInputInterop.TryAccess(device.DevNode,
            LinuxInputInterop.R_OK | LinuxInputInterop.W_OK) == 0;

        if (!writable)
        {
            DiagnosticFix reconnect = new(FixKind.Manual, Loc.Tr("Diagnostics_FixReplugDevice"),
                null, RequiresReconnect: true);

            return Task.FromResult(DiagnosticCheckResult.Warning(Id, Category, title,
                Loc.Tr("Diagnostics_DeviceRuleNotAppliedFmt", device.DevNode), null, reconnect,
                evidence, Loc.Tr("Diagnostics_ValueNotApplied")));
        }

        return Task.FromResult(DiagnosticCheckResult.Pass(Id, Category, title,
            Loc.Tr("Diagnostics_DeviceRuleOk"), null, evidence, Loc.Tr("Diagnostics_ValueComplete")));
    }

    /// <summary>The subsystems this unit's VID/PID has to be listed under.</summary>
    private IEnumerable<string> ExpectedSubsystems()
    {
        yield return "usb";
        yield return "tty";

        // Stream Controller X (1532:0d09): without a readable hidraw node its keys never arrive.
        if (string.Equals(device.Vid, "1532", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(device.Pid, "0d09", StringComparison.OrdinalIgnoreCase))
        {
            yield return "hidraw";
        }
    }

    /// <summary>True when the rule file carries a line for this subsystem and this VID/PID.</summary>
    private bool HasLine(string content, string subsystem)
    {
        foreach (string line in content.Split('\n'))
        {
            string trimmed = line.Trim();

            if (trimmed.StartsWith('#'))
            {
                continue;
            }

            if (trimmed.Contains($"SUBSYSTEM==\"{subsystem}\"", StringComparison.Ordinal) &&
                trimmed.Contains(device.Vid, StringComparison.OrdinalIgnoreCase) &&
                trimmed.Contains(device.Pid, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
