using LoupixDeck.Models.Diagnostics;

namespace LoupixDeck.Services.Diagnostics.Linux;

/// <summary>
/// Runs the Linux Device Doctor checks (issue #258). Every run is read-only and cancellable.
/// </summary>
public interface ILinuxDiagnosticsService
{
    /// <summary>True when this OS has checks to run at all.</summary>
    bool IsSupported { get; }

    /// <summary>The number of checks a full run would execute.</summary>
    int CheckCount { get; }

    /// <summary>The number of checks a run of <paramref name="category"/> would execute.</summary>
    int CountFor(DiagnosticCategory category);

    /// <summary>Runs every check. <paramref name="progress"/> is reported per finished check.</summary>
    Task<DiagnosticRunResult> RunAllAsync(IProgress<DiagnosticCheckResult> progress,
        CancellationToken cancellationToken);

    /// <summary>Runs the checks of a single category.</summary>
    Task<DiagnosticRunResult> RunCategoryAsync(DiagnosticCategory category,
        IProgress<DiagnosticCheckResult> progress, CancellationToken cancellationToken);
}
