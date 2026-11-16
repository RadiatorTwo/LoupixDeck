namespace LoupixDeck.Services.SystemPower;

/// <summary>
/// Shared bookkeeping for the platform lock-state sources: they report what they see, and this
/// turns repeated reports into one event per real change.
/// </summary>
public abstract class SessionLockServiceBase : ISessionLockService
{
    private readonly Lock _gate = new();
    private bool _locked;
    private bool _started;

    public event EventHandler Locked;
    public event EventHandler Unlocked;

    public bool IsSupported { get; private set; }

    public bool IsLocked
    {
        get { lock (_gate) return _locked; }
    }

    public void StartMonitoring()
    {
        lock (_gate)
        {
            if (_started) return;
            _started = true;
        }

        try
        {
            Start();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Lock] Session lock monitoring unavailable: {ex.Message}");
        }
    }

    /// <summary>Attaches to the platform source; called once.</summary>
    protected abstract void Start();

    /// <summary>A source is attached and will report changes.</summary>
    protected void MarkSupported() => IsSupported = true;

    /// <summary>Reports the state the source currently sees; only a change raises an event.</summary>
    protected void Report(bool locked, string source)
    {
        lock (_gate)
        {
            if (_locked == locked) return;
            _locked = locked;
        }

        Console.WriteLine($"[Lock] Session {(locked ? "locked" : "unlocked")} ({source}); " +
                          $"{(locked ? Locked : Unlocked)?.GetInvocationList().Length ?? 0} handler(s).");
        (locked ? Locked : Unlocked)?.Invoke(this, EventArgs.Empty);
    }
}
