using System.Collections.Concurrent;
using System.Diagnostics;
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
/// Registered checks come first, then whatever an <see cref="ILinuxDiagnosticCheckSource"/>
/// builds for the hardware that is currently attached.
///
/// No check may break a run: <see cref="RunOneAsync"/> turns a throw into
/// <see cref="DiagnosticStatus.Unknown"/> and a hang into a timeout.
/// </summary>
public sealed class LinuxDiagnosticsService : ILinuxDiagnosticsService
{
    /// <summary>A single check may not occupy the run for longer than this.</summary>
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(5);

    private readonly IReadOnlyList<ILinuxDiagnosticCheck> _static;
    private readonly IReadOnlyList<ILinuxDiagnosticCheckSource> _sources;

    public LinuxDiagnosticsService(IEnumerable<ILinuxDiagnosticCheck> checks,
        IEnumerable<ILinuxDiagnosticCheckSource> sources)
    {
        _static = checks.ToList();
        _sources = sources.ToList();
    }

    public bool IsSupported => OperatingSystem.IsLinux() && (_static.Count > 0);

    public int CheckCount => Checks().Count;

    public int CountFor(DiagnosticCategory category) => Checks().Count(check => check.Category == category);

    public Task<DiagnosticRunResult> RunAllAsync(IProgress<DiagnosticCheckResult> progress,
        CancellationToken cancellationToken)
        => RunAsync(Checks(), progress, cancellationToken);

    public Task<DiagnosticRunResult> RunCategoryAsync(DiagnosticCategory category,
        IProgress<DiagnosticCheckResult> progress, CancellationToken cancellationToken)
        => RunAsync(Checks().Where(check => check.Category == category).ToList(), progress, cancellationToken);

    public Task<DiagnosticRunResult> RunCheckAsync(string id,
        IProgress<DiagnosticCheckResult> progress, CancellationToken cancellationToken)
        => RunAsync(Checks().Where(check => check.Id == id).ToList(), progress, cancellationToken);

    /// <summary>
    /// The checks of this moment: the registered ones, then whatever the sources build for the
    /// hardware that is plugged in right now. Rebuilt per call rather than cached, so a deck
    /// connected after the last run is diagnosed without a restart.
    /// </summary>
    private IReadOnlyList<ILinuxDiagnosticCheck> Checks()
    {
        List<ILinuxDiagnosticCheck> checks = [.. _static];

        foreach (ILinuxDiagnosticCheckSource source in _sources)
        {
            try
            {
                checks.AddRange(source.CreateChecks());
            }
            catch (Exception)
            {
                // A source that cannot enumerate must not take the run down; its checks are
                // simply absent, exactly as if no hardware were attached.
            }
        }

        return checks;
    }

    /// <summary>
    /// Hands the run to the thread pool before anything else happens. Most checks are synchronous
    /// and return an already-completed task, so a run started from the UI thread would execute
    /// end to end on it - blocking the window and holding back every progress callback until the
    /// whole run is over, which showed up as an empty progress bar and a page that was only
    /// complete on the second run.
    /// </summary>
    private Task<DiagnosticRunResult> RunAsync(IReadOnlyList<ILinuxDiagnosticCheck> selection,
        IProgress<DiagnosticCheckResult> progress, CancellationToken cancellationToken)
        => Task.Run(() => RunCoreAsync(selection, progress, cancellationToken), CancellationToken.None);

    private async Task<DiagnosticRunResult> RunCoreAsync(IReadOnlyList<ILinuxDiagnosticCheck> selection,
        IProgress<DiagnosticCheckResult> progress, CancellationToken cancellationToken)
    {
        if (!IsSupported || (selection.Count == 0))
        {
            return new DiagnosticRunResult(DateTimeOffset.Now, false, []);
        }

        ConcurrentDictionary<string, DiagnosticCheckResult> results = new();

        List<ILinuxDiagnosticCheck> parallel = selection.Where(check => check is not IExclusiveDiagnosticCheck).ToList();
        List<ILinuxDiagnosticCheck> serial = selection.Where(check => check is IExclusiveDiagnosticCheck).ToList();

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

        long startedAt = Stopwatch.GetTimestamp();

        try
        {
            DiagnosticCheckResult result = await check.RunAsync(timeout.Token);

            result ??= DiagnosticCheckResult.Unknown(check.Id, check.Category, title,
                Loc.Tr("Diagnostics_CheckFailedToRun"));

            return result with { Duration = Stopwatch.GetElapsedTime(startedAt) };
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
