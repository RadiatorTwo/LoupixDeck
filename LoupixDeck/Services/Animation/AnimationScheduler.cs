using System.Diagnostics;

namespace LoupixDeck.Services.Animation;

/// <summary>
/// Per-device central animation loop (issue #119). A single background task paces every
/// registered <see cref="IAnimationSource"/>:
///
/// <list type="bullet">
///   <item>one shared high-resolution clock (<see cref="Stopwatch"/>) drives all sources,
///         so their timing never drifts apart;</item>
///   <item>each source is ticked at its own <see cref="IAnimationSource.TargetFps"/>,
///         clamped to <see cref="GlobalFpsLimit"/>;</item>
///   <item>inactive sources (<see cref="IAnimationSource.IsActive"/> == false) are not
///         ticked — pausing animations for non-current pages costs nothing;</item>
///   <item>a source whose previous frame is still rendering is skipped rather than queued,
///         so a slow render lowers that source's rate instead of piling work up;</item>
///   <item>the loop parks (no busy-spin) when nothing is due and is woken by
///         <see cref="Register"/> / <see cref="RequestFrame"/> / a completed frame.</item>
/// </list>
///
/// Sources own their render targets and their dirty-state; the scheduler only owns timing.
/// Render callbacks run off the UI thread (Skia work still goes through
/// <see cref="LoupixDeck.Utils.SkiaRenderGate.Sync"/>, UI mutations are posted), matching the
/// rest of the device pipeline.
/// </summary>
public sealed class AnimationScheduler : IAnimationScheduler, IDisposable
{
    private const int DefaultGlobalFpsLimit = 30;
    private const int MaxGlobalFpsLimit = 120;

    // While at least one source is registered the loop parks at most this long even when
    // nothing is due, so an IsActive flip that wasn't pushed via RequestFrame is still
    // noticed promptly. Cheap insurance — real activations should call RequestFrame.
    private static readonly TimeSpan IdlePoll = TimeSpan.FromMilliseconds(250);

    // Never sleep shorter than this; protects against a runaway tight loop if a source
    // ever reports an absurdly high TargetFps.
    private static readonly TimeSpan MinSleep = TimeSpan.FromMilliseconds(1);

    private sealed class Registration(IAnimationSource source)
    {
        public IAnimationSource Source { get; } = source;
        public long StartTimestamp { get; set; }
        public long LastFrameTimestamp { get; set; }
        public TimeSpan NextDue { get; set; }
        public long FrameNumber { get; set; }

        // 0 = idle, 1 = a frame is currently rendering. Guards against re-entrant ticks.
        public int InFlight;
    }

    private readonly Lock _gate = new();
    private readonly List<Registration> _registrations = [];
    private readonly SemaphoreSlim _wake = new(0, 1);

    private long _schedulerStart;
    private int _globalFpsLimit = DefaultGlobalFpsLimit;
    private CancellationTokenSource _cts;
    private Task _loopTask;
    private bool _disposed;

    public int GlobalFpsLimit => Volatile.Read(ref _globalFpsLimit);

    public void Register(IAnimationSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        lock (_gate)
        {
            if (_disposed) return;
            if (_registrations.Any(r => ReferenceEquals(r.Source, source)))
                return;

            // Establish the loop clock FIRST: EnsureLoopRunning resets _schedulerStart on a
            // cold start, so computing NextDue beforehand would measure it against the old
            // (or zero) base — yielding a NextDue far in the future that ElapsedNow() never
            // catches up to, so this source would never be ticked (the screensaver, usually
            // the only/first source at idle time, hit this every launch). Ordering it after
            // the reset makes NextDue ≈ now and the first frame paints immediately.
            EnsureLoopRunning();

            var now = ElapsedNow();
            _registrations.Add(new Registration(source)
            {
                StartTimestamp = Stopwatch.GetTimestamp(),
                LastFrameTimestamp = Stopwatch.GetTimestamp(),
                NextDue = now, // due immediately so it paints its first frame without delay
            });
        }

        Signal();
    }

    public void Unregister(IAnimationSource source)
    {
        if (source == null) return;

        lock (_gate)
        {
            _registrations.RemoveAll(r => ReferenceEquals(r.Source, source));
        }

        Signal();
    }

    public void SetGlobalFpsLimit(int fps)
    {
        if (fps <= 0) return;
        Volatile.Write(ref _globalFpsLimit, Math.Min(fps, MaxGlobalFpsLimit));
        Signal();
    }

    public void RequestFrame(IAnimationSource source)
    {
        if (source == null) return;

        lock (_gate)
        {
            if (_disposed) return;
            var reg = _registrations.FirstOrDefault(r => ReferenceEquals(r.Source, source));
            if (reg == null) return;

            // Establish the loop clock before reading it (see Register): on a cold restart
            // EnsureLoopRunning rebases _schedulerStart, so NextDue must be computed after it.
            EnsureLoopRunning();
            // Pull its next due time forward to "now" so the loop ticks it on the next pass.
            reg.NextDue = ElapsedNow();
        }

        Signal();
    }

    public void Stop()
    {
        CancellationTokenSource cts;
        lock (_gate)
        {
            cts = _cts;
            _cts = null;
            _loopTask = null;
        }

        try { cts?.Cancel(); } catch { /* already disposed */ }
        cts?.Dispose();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _registrations.Clear();
        }

        Stop();
        _wake.Dispose();
    }

    // ───────────────────────── loop ─────────────────────────

    /// <summary>Caller must hold <see cref="_gate"/>. Starts the loop if it isn't running.</summary>
    private void EnsureLoopRunning()
    {
        if (_disposed || _loopTask != null) return;

        _schedulerStart = Stopwatch.GetTimestamp();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loopTask = Task.Run(() => RunLoop(token), token);
    }

    private async Task RunLoop(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var sleep = Tick(token);

                // No active sources at all → the loop has nothing to do; let it exit so we
                // don't hold a parked task forever. A later Register/RequestFrame restarts it.
                if (sleep == TimeSpan.MaxValue)
                {
                    if (TryStopIfNoRegistrations())
                        return;
                    sleep = IdlePoll;
                }

                if (sleep < MinSleep) sleep = MinSleep;

                // WaitAsync returns early (true) when signalled, or on timeout (false); both
                // just loop again. The permit count is capped at 1, so bursts of signals
                // collapse into a single early wake — exactly the coalescing we want.
                await _wake.WaitAsync(sleep, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // expected on Stop/Dispose
        }
        catch (Exception ex)
        {
            Console.WriteLine($"AnimationScheduler loop error: {ex.Message}");
        }
    }

    /// <summary>
    /// Dispatches every due, active, not-in-flight source and returns how long to sleep
    /// until the next one is due. Returns <see cref="TimeSpan.MaxValue"/> when no source is
    /// currently active (the caller decides whether to park or stop).
    /// </summary>
    private TimeSpan Tick(CancellationToken token)
    {
        Registration[] snapshot;
        int globalLimit;
        lock (_gate)
        {
            if (_registrations.Count == 0) return TimeSpan.MaxValue;
            snapshot = _registrations.ToArray();
            globalLimit = _globalFpsLimit;
        }

        var now = ElapsedNow();
        var nextWake = TimeSpan.MaxValue;
        var anyActive = false;

        foreach (var reg in snapshot)
        {
            bool active;
            int targetFps;
            try
            {
                active = reg.Source.IsActive;
                targetFps = reg.Source.TargetFps;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"AnimationScheduler: source state read threw: {ex.Message}");
                continue;
            }

            if (!active)
            {
                // Reset due to "now" so a reactivated source fires immediately rather than
                // replaying a backlog of missed frames.
                reg.NextDue = now;
                continue;
            }

            anyActive = true;

            // Still rendering the previous frame → skip; its completion re-signals the loop.
            if (Volatile.Read(ref reg.InFlight) != 0)
                continue;

            var interval = IntervalFor(targetFps, globalLimit);

            if (now >= reg.NextDue)
            {
                Dispatch(reg, interval, token);

                // Advance one interval to keep cadence; snap forward if we fell behind.
                reg.NextDue += interval;
                if (reg.NextDue <= now)
                    reg.NextDue = now + interval;
            }

            var until = reg.NextDue - now;
            if (until < nextWake) nextWake = until;
        }

        return anyActive ? nextWake : TimeSpan.MaxValue;
    }

    private void Dispatch(Registration reg, TimeSpan interval, CancellationToken token)
    {
        Volatile.Write(ref reg.InFlight, 1);

        var startTs = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(reg.StartTimestamp, startTs);
        var delta = Stopwatch.GetElapsedTime(reg.LastFrameTimestamp, startTs);
        reg.LastFrameTimestamp = startTs;

        var effectiveFps = (int)Math.Round(1.0 / interval.TotalSeconds);
        var ctx = new AnimationRenderContext(reg.FrameNumber++, elapsed, delta, effectiveFps, token);

        // Fire the render without blocking the loop. On completion we clear the in-flight
        // flag and wake the loop so the next frame can be scheduled promptly.
        _ = RenderAndComplete(reg, ctx);
    }

    private async Task RenderAndComplete(Registration reg, AnimationRenderContext ctx)
    {
        try
        {
            await reg.Source.RenderFrameAsync(ctx).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // expected when stopping / unregistering mid-frame
        }
        catch (Exception ex)
        {
            Console.WriteLine($"AnimationScheduler: source frame threw: {ex.Message}");
        }
        finally
        {
            Volatile.Write(ref reg.InFlight, 0);
            Signal();
        }
    }

    /// <summary>Stops the loop iff there are no registrations left. Returns true when it stopped.</summary>
    private bool TryStopIfNoRegistrations()
    {
        lock (_gate)
        {
            if (_registrations.Count != 0) return false;
            _loopTask = null;
            var cts = _cts;
            _cts = null;
            // Dispose outside isn't necessary; cancel so any await on this token unwinds.
            try { cts?.Cancel(); } catch { /* ignore */ }
            cts?.Dispose();
            return true;
        }
    }

    private TimeSpan IntervalFor(int targetFps, int globalLimit)
    {
        var fps = targetFps > 0 ? Math.Min(targetFps, globalLimit) : globalLimit;
        if (fps <= 0) fps = DefaultGlobalFpsLimit;
        return TimeSpan.FromSeconds(1.0 / fps);
    }

    private TimeSpan ElapsedNow() => Stopwatch.GetElapsedTime(_schedulerStart);

    /// <summary>Releases the wake permit if one isn't already pending (count capped at 1).</summary>
    private void Signal()
    {
        try { _wake.Release(); }
        catch (SemaphoreFullException) { /* a wake is already pending */ }
        catch (ObjectDisposedException) { /* disposed */ }
    }
}
