namespace LoupixDeck.Services.AppLauncher;

/// <summary>
/// Caching, de-duplication and ordering shared by the per-OS discovery backends, so each one only
/// has to enumerate its own sources.
/// </summary>
public abstract class AppDiscoveryServiceBase : IAppDiscoveryService
{
    private readonly Lock _sync = new();
    private Task<IReadOnlyList<InstalledApp>> _scan;

    public abstract bool IsSupported { get; }

    public Task<IReadOnlyList<InstalledApp>> GetAppsAsync(CancellationToken cancellationToken = default)
    {
        // Caching the task itself means concurrent callers share one scan and later callers get a
        // completed task back. The lock matters: '??=' alone would let two first callers each start
        // a full scan.
        lock (_sync)
        {
            return _scan ??= StartScan(cancellationToken);
        }
    }

    public Task<IReadOnlyList<InstalledApp>> RefreshAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return _scan = StartScan(cancellationToken);
        }
    }

    private Task<IReadOnlyList<InstalledApp>> StartScan(CancellationToken cancellationToken)
        => Task.Run(() => Collect(cancellationToken), cancellationToken);

    /// <summary>Enumerates this platform's sources. Called on a thread-pool thread; may block.</summary>
    protected abstract IEnumerable<InstalledApp> Discover(CancellationToken cancellationToken);

    private IReadOnlyList<InstalledApp> Collect(CancellationToken cancellationToken)
    {
        Dictionary<string, InstalledApp> unique = new(StringComparer.Ordinal);

        foreach (InstalledApp app in Discover(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (app == null || string.IsNullOrWhiteSpace(app.Name) || string.IsNullOrWhiteSpace(app.Target))
                continue;

            // Keyed on the launch target, so the same program found twice collapses while two
            // different programs that happen to share a display name both survive.
            if (unique.TryGetValue(app.Identity, out InstalledApp existing) && !Supersedes(app, existing))
                continue;

            unique[app.Identity] = app;
        }

        return unique.Values
            .OrderByDescending(app => app.IsGame)
            .ThenBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Which of two entries for the same target wins. An entry that brings artwork or an icon
    /// source is more useful than a bare one, so a launcher entry beats a plain shortcut.
    /// </summary>
    private static bool Supersedes(InstalledApp candidate, InstalledApp existing)
    {
        int candidateScore = Score(candidate);
        int existingScore = Score(existing);
        return candidateScore > existingScore;

        static int Score(InstalledApp app)
        {
            int score = 0;
            if (!string.IsNullOrEmpty(app.PreResolvedIcon)) score += 2;
            if (!string.IsNullOrEmpty(app.IconExtractSource)) score += 1;
            return score;
        }
    }
}