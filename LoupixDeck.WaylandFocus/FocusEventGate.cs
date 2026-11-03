namespace LoupixDeck.WaylandFocus;

/// <summary>
/// Shapes a raw focus stream into what the app-switching pipeline consumes:
/// resolves <see cref="FocusInfo.ProcessName"/> once per app_id and publishes
/// only events that change the current (process, title) pair.
/// <para>
/// Why this exists: on COSMIC the compositor reports no PID, so every resolution
/// is a full /proc scan plus a .desktop match — and titles churn constantly
/// (terminals and browsers rename their windows all the time). The cache holds
/// pid-less resolutions only, keyed by app_id, with the same 5-minute TTL idea
/// as <c>ProcessNamer</c>'s desktop cache: a rename or a newly installed desktop
/// file becomes resolvable again on expiry, and a hard entry cap bounds memory
/// for the process lifetime however long it runs or however many app_ids appear.
/// <para>
/// PID-bearing events never consult the cache: a PID is ground truth in one
/// cheap /proc read, and an app_id is only a client claim — wine windows and
/// mis-set ids would otherwise be answered from a stale entry.
/// </summary>
public sealed class FocusEventGate
{
    // Same tradeoff as ProcessNamer.DesktopCache: desktop files change on installs,
    // so a short staleness window buys a cheap event path and heals wrong entries.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    // Structural memory ceiling: entries are two small strings, so this tops out around
    // 50 KB no matter how long the process lives or what a client claims as its app_id.
    private const int CacheCapacity = 256;

    private readonly record struct CacheEntry(string Name, DateTime Expires);

    private readonly Func<FocusInfo, string?> _resolve;
    private readonly Func<DateTime> _clock;
    private readonly int _capacity;
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

    private string? _lastProcess;
    private string? _lastTitle;

    /// <summary>Production wiring: resolve through <see cref="ProcessNamer"/>.</summary>
    public FocusEventGate()
        : this(focus => ProcessNamer.Enrich(focus).ProcessName, static () => DateTime.UtcNow,
            CacheCapacity)
    {
    }

    /// <summary>Test seam: injectable resolver, clock and cache capacity.</summary>
    internal FocusEventGate(Func<FocusInfo, string?> resolve, Func<DateTime> clock,
        int capacity = CacheCapacity)
    {
        _resolve = resolve;
        _clock = clock;
        _capacity = capacity;
    }

    /// <summary>
    /// The event to publish — the focus snapshot with <see cref="FocusInfo.ProcessName"/>
    /// resolved — or null when the event adds nothing the pipeline has not seen.
    /// </summary>
    public FocusInfo? Take(FocusInfo focus)
    {
        string appId = focus.AppId ?? string.Empty;
        string title = focus.Title ?? string.Empty;
        string process = Resolve(focus, appId);

        // Two app_ids resolving to the same binary are one window as far as the
        // pipeline is concerned; a suppressed event leaves all state untouched.
        if ((process == _lastProcess) && (title == _lastTitle))
        {
            return null;
        }

        _lastProcess = process;
        _lastTitle = title;

        return focus with { ProcessName = process };
    }

    private string Resolve(FocusInfo focus, string appId)
    {
        // Ground truth in one cheap read — always live, never cached (see class docs).
        if (focus.Pid is not null)
        {
            return _resolve(focus) ?? string.Empty;
        }

        if (appId.Length > 0 &&
            _cache.TryGetValue(appId, out CacheEntry hit) &&
            (hit.Expires > _clock()))
        {
            return hit.Name;
        }

        string name = _resolve(focus) ?? string.Empty;

        if (appId.Length > 0)
        {
            if (_cache.Count >= _capacity)
            {
                // Cheap structural bound; normal sessions never approach the cap, and a
                // client cycling app_ids already pays a scan per event either way.
                _cache.Clear();
            }

            _cache[appId] = new CacheEntry(name, _clock() + CacheTtl);
        }

        return name;
    }
}