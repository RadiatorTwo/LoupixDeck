using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;

namespace LoupixDeck.Services.Diagnostics.Linux.Checks.Devices;

/// <summary>
/// Builds the Device Access checks for the decks that are attached right now (issue #258
/// phase 2). Every unit gets its own set, so two identical decks are diagnosed separately.
///
/// With nothing attached the category is not empty: it carries the one check that says so,
/// because "no deck found" is the most common report of all and has to be visible.
/// </summary>
public sealed class DeviceCheckSource(IDeviceHostRegistry hosts) : ILinuxDiagnosticCheckSource
{
    /// <summary>
    /// How long an enumeration is reused. Enumerating runs udevadm once per serial node, and a
    /// single run asks for the checks twice - once to count them, once to run them. Anything
    /// longer would start hiding a deck that was just plugged in.
    /// </summary>
    private static readonly TimeSpan EnumerationFreshness = TimeSpan.FromSeconds(3);

    private readonly Lock _gate = new();

    private IReadOnlyList<LinuxDeckDevice> _cached;
    private DateTime _cachedAt;

    public IReadOnlyList<ILinuxDiagnosticCheck> CreateChecks()
    {
        if (!OperatingSystem.IsLinux())
        {
            return [];
        }

        IReadOnlyList<LinuxDeckDevice> decks = Enumerate();

        if (decks.Count == 0)
        {
            return [new NoDeviceCheck()];
        }

        List<ILinuxDiagnosticCheck> checks = [];

        foreach (LinuxDeckDevice deck in decks)
        {
            checks.Add(new DeviceIdentityCheck(deck));
            checks.Add(new DeviceNodeAccessCheck(deck));
            checks.Add(new DeviceUdevRuleCheck(deck));
            checks.Add(new DevicePortOwnerCheck(deck));
            checks.Add(new DeviceLinkCheck(deck, hosts));
            checks.Add(new DeviceLastConnectionCheck(deck));
        }

        return checks;
    }

    private IReadOnlyList<LinuxDeckDevice> Enumerate()
    {
        lock (_gate)
        {
            if ((_cached != null) && (DateTime.UtcNow - _cachedAt < EnumerationFreshness))
            {
                return _cached;
            }

            _cached = LinuxDeckDeviceFacts.Enumerate();
            _cachedAt = DateTime.UtcNow;

            return _cached;
        }
    }
}

/// <summary>The stand-in for an empty Device Access category: no supported deck was found.</summary>
internal sealed class NoDeviceCheck : ILinuxDiagnosticCheck
{
    public string Id => "device.none";

    public DiagnosticCategory Category => DiagnosticCategory.DeviceAccess;

    public Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
        => Task.FromResult(DiagnosticCheckResult.Warning(Id, Category, DiagnosticCheckTitles.For(Id),
            Loc.Tr("Diagnostics_NoDeviceFound"), null,
            new DiagnosticFix(FixKind.Manual, Loc.Tr("Diagnostics_FixCheckCableAndPower")), null,
            Loc.Tr("Diagnostics_ValueNone")));
}
