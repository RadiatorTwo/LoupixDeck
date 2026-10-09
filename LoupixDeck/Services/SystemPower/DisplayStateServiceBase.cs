namespace LoupixDeck.Services.SystemPower;

/// <summary>
/// Shared bookkeeping for the platform display-state sources: they only report the state they
/// see, and this turns repeated reports into one event per real change. Sources re-announce the
/// current state on registration or deliver "dimmed" next to "on", which must not repaint the
/// device again.
/// </summary>
public abstract class DisplayStateServiceBase : IDisplayStateService
{
    private readonly Lock _gate = new();
    private bool _displaysOn = true;
    private bool _screenSaverRunning;
    private bool _started;

    public event EventHandler DisplaysOff;
    public event EventHandler DisplaysOn;
    public event EventHandler ScreenSaverStarted;
    public event EventHandler ScreenSaverStopped;

    public bool IsSupported { get; private set; }

    public bool IsScreenSaverSupported { get; private set; }

    public bool ScreenSaverRunning
    {
        get { lock (_gate) return _screenSaverRunning; }
    }

    public bool DisplaysAreOn
    {
        get { lock (_gate) return _displaysOn; }
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
            Console.WriteLine($"[Displays] Display state monitoring unavailable: {ex.Message}");
        }
    }

    /// <summary>Attaches to the platform source; called once.</summary>
    protected abstract void Start();

    /// <summary>A source is attached and will report changes.</summary>
    protected void MarkSupported() => IsSupported = true;

    /// <summary>The desktop screen saver is followed as well.</summary>
    protected void MarkScreenSaverSupported() => IsScreenSaverSupported = true;

    /// <summary>Reports the state the source currently sees; only a change raises an event.</summary>
    protected void Report(bool displaysOn)
    {
        lock (_gate)
        {
            if (_displaysOn == displaysOn) return;
            _displaysOn = displaysOn;
        }

        Console.WriteLine($"[Displays] Displays {(displaysOn ? "on" : "off")}");
        (displaysOn ? DisplaysOn : DisplaysOff)?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reports whether the desktop screen saver runs; only a change raises an event.</summary>
    protected void ReportScreenSaver(bool running)
    {
        lock (_gate)
        {
            if (_screenSaverRunning == running) return;
            _screenSaverRunning = running;
        }

        Console.WriteLine($"[Displays] Desktop screen saver {(running ? "started" : "stopped")}");
        (running ? ScreenSaverStarted : ScreenSaverStopped)?.Invoke(this, EventArgs.Empty);
    }
}
