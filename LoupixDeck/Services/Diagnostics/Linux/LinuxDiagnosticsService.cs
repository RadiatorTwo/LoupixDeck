using System.Collections.Concurrent;
using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;

namespace LoupixDeck.Services.Diagnostics.Linux;

/// <summary>
/// Runs the registered <see cref="ILinuxDiagnosticCheck"/>s (issue #258).
///
/// Independent read-only checks run in parallel; checks marked
/// <see cref="IExclusiveDiagnosticCheck"/> touch a kernel device node and run serially
/// afterwards, so a uinput probe and an evdev sweep can never overlap. Results are returned in
/// registration order, so the page and the report read the same way on every run.
///
/// No check may break a run: <see cref="RunOneAsync"/> turns a throw into
/// <see cref="DiagnosticStatus.Unknown"/> and a hang into a timeout.
/// </summary>
public sealed class LinuxDiagnosticsService : ILinuxDiagnosticsService
{
    /// <summary>A single check may not occupy the run for longer than this.</summary>
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(5);

    private readonly IReadOnlyList<ILinuxDiagnosticCheck> _checks;
    private readonly IReadOnlyList<ILinuxDiagnosticCheck> _parallel;
    private readonly IReadOnlyList<ILinuxDiagnosticCheck> _serial;

    public LinuxDiagnosticsService(IEnumerable<ILinuxDiagnosticCheck> checks)
    {
        _checks = checks.ToList();
        _parallel = _checks.Where(check => check is not IExclusiveDiagnosticCheck).ToList();
        _serial = _checks.Where(check => check is IExclusiveDiagnosticCheck).ToList();
    }

    public bool IsSupported => OperatingSystem.IsLinux() && (_checks.Count > 0);

    public int CheckCount => _checks.Count;

    public int CountFor(DiagnosticCategory category) => _checks.Count(check => check.Category == category);

    public Task<DiagnosticRunResult> RunAllAsync(IProgress<DiagnosticCheckResult> progress,
        CancellationToken cancellationToken)
        => RunAsync(_checks, progress, cancellationToken);

    public Task<DiagnosticRunResult> RunCategoryAsync(DiagnosticCategory category,
        IProgress<DiagnosticCheckResult> progress, CancellationToken cancellationToken)
        => RunAsync(_checks.Where(check => check.Category == category).ToList(), progress, cancellationToken);

    private async Task<DiagnosticRunResult> RunAsync(IReadOnlyList<ILinuxDiagnosticCheck> selection,
        IProgress<DiagnosticCheckResult> progress, CancellationToken cancellationToken)
    {
        if (!IsSupported || (selection.Count == 0))
        {
            return new DiagnosticRunResult(DateTimeOffset.Now, false, []);
        }

        ConcurrentDictionary<string, DiagnosticCheckResult> results = new();

        List<ILinuxDiagnosticCheck> parallel = selection.Where(_parallel.Contains).ToList();
        List<ILinuxDiagnosticCheck> serial = selection.Where(_serial.Contains).ToList();

        await Task.WhenAll(parallel.Select(check => RunAndCollectAsync(check, results, progress, cancellationToken)));

        // Device-node probes run one after another, after everything else has finished.
        foreach (ILinuxDiagnosticCheck check in serial)
        {
            await RunAndCollectAsync(check, results, progress, cancellationToken);
        }

        List<DiagnosticCheckResult> ordered = selection
            .Select(check => results.TryGetValue(check.Id, out DiagnosticCheckResult result) ? result : null)
            .Where(result => result != null)
            .ToList();

        return new DiagnosticRunResult(DateTimeOffset.Now, cancellationToken.IsCancellationRequested, ordered);
    }

    private static async Task RunAndCollectAsync(ILinuxDiagnosticCheck check,
        ConcurrentDictionary<string, DiagnosticCheckResult> results,
        IProgress<DiagnosticCheckResult> progress, CancellationToken cancellationToken)
    {
        DiagnosticCheckResult result = await RunOneAsync(check, cancellationToken);
        results[check.Id] = result;
        progress?.Report(result);
    }

    /// <summary>
    /// The containment boundary. A check that throws, hangs or returns nothing becomes an
    /// <see cref="DiagnosticStatus.Unknown"/> row instead of taking the whole run down.
    /// </summary>
    private static async Task<DiagnosticCheckResult> RunOneAsync(ILinuxDiagnosticCheck check,
        CancellationToken cancellationToken)
    {
        string title = DiagnosticCheckTitles.For(check.Id);

        if (cancellationToken.IsCancellationRequested)
        {
            return DiagnosticCheckResult.Skipped(check.Id, check.Category, title,
                Loc.Tr("Diagnostics_Cancelled"));
        }

        using CancellationTokenSource timeout =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CheckTimeout);

        try
        {
            DiagnosticCheckResult result = await check.RunAsync(timeout.Token);

            return result ?? DiagnosticCheckResult.Unknown(check.Id, check.Category, title,
                Loc.Tr("Diagnostics_CheckFailedToRun"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The user stopped the run. That says nothing about the system, so it is not a failure.
            return DiagnosticCheckResult.Skipped(check.Id, check.Category, title,
                Loc.Tr("Diagnostics_Cancelled"));
        }
        catch (OperationCanceledException)
        {
            return DiagnosticCheckResult.Unknown(check.Id, check.Category, title,
                Loc.Tr("Diagnostics_TimedOut"));
        }
        catch (Exception ex)
        {
            return DiagnosticCheckResult.Unknown(check.Id, check.Category, title,
                Loc.Tr("Diagnostics_CheckFailedToRun"), $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
