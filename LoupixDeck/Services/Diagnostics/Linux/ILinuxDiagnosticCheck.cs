using LoupixDeck.Models.Diagnostics;

namespace LoupixDeck.Services.Diagnostics.Linux;

/// <summary>
/// One isolated Linux diagnostic check (issue #258). A check is read-only: it inspects the
/// system and reports, it never repairs anything. It must not throw - the orchestrator turns a
/// thrown exception into <see cref="DiagnosticStatus.Unknown"/>, but a check that maps its own
/// failure modes produces a far better message.
/// </summary>
public interface ILinuxDiagnosticCheck
{
    /// <summary>Stable identifier, for example "uinput.write-access".</summary>
    string Id { get; }

    /// <summary>The category the check is grouped under.</summary>
    DiagnosticCategory Category { get; }

    /// <summary>Runs the check. Must honour <paramref name="cancellationToken"/>.</summary>
    Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Marker for a check that touches a kernel device node (/dev/uinput, /dev/input/event*).
/// Those run serially, after the parallel group, so two probes cannot collide with each other
/// or with a device that is being initialized at the same time.
/// </summary>
public interface IExclusiveDiagnosticCheck;
