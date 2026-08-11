namespace LoupixDeck.Services.Macros;

/// <summary>
/// One physical button or touch contact that is currently held down and has started a
/// macro (#185). The token is created at press time, published to the macro run through
/// <see cref="TriggerPressScope"/>, and completed when the release event arrives — which
/// lets a macro hold a key for exactly as long as the user holds the button.
///
/// A release event can be lost (a TOUCH_END dropped by a framing resync, a disconnect
/// mid-press), so every token carries its own watchdog: after
/// <see cref="DefaultWatchdogMilliseconds"/> the press counts as released no matter what.
/// Without it a "wait forever" step could keep a modifier key down indefinitely.
/// </summary>
public sealed class TriggerPress
{
    /// <summary>How long a press may stay held before it is treated as released anyway.</summary>
    public const int DefaultWatchdogMilliseconds = 30_000;

    // RunContinuationsAsynchronously is load-bearing, not a style choice: Release() runs on
    // the serial-read thread, and a waiting macro's continuation issues device calls whose
    // completion is signalled by that very thread. Resuming it inline would deadlock the
    // device exactly as described on LoupedeckLiveSController.FireAndForget.
    private readonly TaskCompletionSource _released =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly int _watchdogMilliseconds;
    private Timer _watchdog;

    public TriggerPress(string source, int watchdogMilliseconds = DefaultWatchdogMilliseconds)
    {
        Source = source ?? string.Empty;
        _watchdogMilliseconds = watchdogMilliseconds;

        if (watchdogMilliseconds > 0)
        {
            _watchdog = new Timer(static state => ((TriggerPress)state).OnWatchdog(), this,
                watchdogMilliseconds, Timeout.Infinite);
        }
    }

    /// <summary>Human-readable origin ("Button:BUTTON0", "Touch:3") — diagnostics only.</summary>
    public string Source { get; }

    /// <summary>True while the button / touch is still down.</summary>
    public bool IsHeld => !_released.Task.IsCompleted;

    /// <summary>Completes once the press ends. Never faults, never cancels.</summary>
    public Task Released => _released.Task;

    /// <summary>
    /// Ends the press. Idempotent and thread-safe: a late real release event after the
    /// watchdog already fired (or a second call from a force-release path) is a no-op.
    /// </summary>
    public void Release()
    {
        if (!_released.TrySetResult())
            return;

        // Disposing from inside the timer's own callback is legal.
        Interlocked.Exchange(ref _watchdog, null)?.Dispose();
    }

    private void OnWatchdog()
    {
        if (!IsHeld)
            return;

        Console.WriteLine(
            $"[TriggerPress] {Source} watchdog fired after {_watchdogMilliseconds} ms — treating as released.");
        Release();
    }
}
