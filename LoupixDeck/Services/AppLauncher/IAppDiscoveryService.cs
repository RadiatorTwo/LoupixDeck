namespace LoupixDeck.Services.AppLauncher;

/// <summary>
/// Finds the applications installed on this machine. Per-OS implementations mirror
/// <c>IActiveWindowMonitor</c>: Start Menu plus the game launchers on Windows, XDG desktop entries
/// on Linux, and a no-op everywhere else.
/// </summary>
public interface IAppDiscoveryService
{
    /// <summary>
    /// True when this platform can enumerate applications at all. False means the picker should say
    /// so rather than present an empty list.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// The installed applications, scanned on first call and cached afterwards. Concurrent callers
    /// share one scan. Always runs off the calling thread — a cold scan walks thousands of files.
    /// </summary>
    Task<IReadOnlyList<InstalledApp>> GetAppsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Discards the cache and scans again, for when the user has installed something while the app
    /// was running. Returns the fresh list.
    /// </summary>
    Task<IReadOnlyList<InstalledApp>> RefreshAsync(CancellationToken cancellationToken = default);
}

/// <summary>Fallback for platforms with no discovery backend.</summary>
public sealed class NoOpAppDiscoveryService : IAppDiscoveryService
{
    public bool IsSupported => false;

    public Task<IReadOnlyList<InstalledApp>> GetAppsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<InstalledApp>>([]);

    public Task<IReadOnlyList<InstalledApp>> RefreshAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<InstalledApp>>([]);
}