namespace LoupixDeck.Services.SystemPower;

/// <summary>
/// Wraps a platform power service with a wall-clock check, because the OS notification
/// cannot be relied on: on a Modern-Standby (S0) machine Windows often delivers neither
/// <c>Suspend</c> nor <c>Resume</c>, and on Linux the logind signal is missing whenever
/// dbus-monitor is unavailable. Without a resume event nothing re-establishes the link
/// after wake and the device just stays dark (issue #195).
///
/// The detection is deliberately dumb: a timer that ticks every few seconds cannot tick
/// while the host is suspended, so a tick that arrives far later than scheduled means
/// wall-clock time passed without us running — i.e. the machine was asleep. A resume the
/// platform service already reported is not reported a second time.
/// </summary>
public sealed class ResumeDetectingSystemPowerService(ISystemPowerService inner) : ISystemPowerService, IDisposable
{
    /// <summary>How often the wall clock is sampled.</summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(5);

    /// <summary>A tick this much later than scheduled counts as "the host was suspended".
    /// Well above any scheduling delay or GC pause, so normal operation never trips it.</summary>
    private static readonly TimeSpan SuspendGap = TimeSpan.FromSeconds(25);

    /// <summary>A gap right after the platform service reported the resume itself is the
    /// same wake — reported once, not twice.</summary>
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(60);

    public event EventHandler Suspending;
    public event EventHandler Resuming;

    private Timer _timer;
    private DateTime _lastTickUtc;
    private DateTime _lastResumeUtc = DateTime.MinValue;
    private bool _started;

    public void StartMonitoring()
    {
        if (_started) return;
        _started = true;

        inner.Suspending += OnInnerSuspending;
        inner.Resuming += OnInnerResuming;
        inner.StartMonitoring();

        _lastTickUtc = DateTime.UtcNow;
        _timer = new Timer(_ => Tick(), null, TickInterval, TickInterval);
    }

    private void OnInnerSuspending(object sender, EventArgs e)
    {
        Console.WriteLine("[Power] suspend reported by the OS");
        Suspending?.Invoke(this, EventArgs.Empty);
    }

    private void OnInnerResuming(object sender, EventArgs e)
    {
        Console.WriteLine("[Power] resume reported by the OS");
        RaiseResume();
    }

    private void Tick()
    {
        var now = DateTime.UtcNow;
        var gap = now - _lastTickUtc;
        _lastTickUtc = now;

        if (gap < SuspendGap) return;

        // The platform service may have reported this very wake moments ago.
        if (now - _lastResumeUtc < DuplicateWindow)
        {
            Console.WriteLine($"[Power] {gap.TotalSeconds:F0}s gap — already handled as a resume");
            return;
        }

        Console.WriteLine($"[Power] {gap.TotalSeconds:F0}s gap in the wall clock — treating it as a resume");
        RaiseResume();
    }

    private void RaiseResume()
    {
        _lastResumeUtc = DateTime.UtcNow;
        _lastTickUtc = DateTime.UtcNow;
        Resuming?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
        inner.Suspending -= OnInnerSuspending;
        inner.Resuming -= OnInnerResuming;
        (inner as IDisposable)?.Dispose();
    }
}
