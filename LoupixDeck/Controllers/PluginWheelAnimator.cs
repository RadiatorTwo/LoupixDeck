using System.Diagnostics;
using LoupixDeck.Models;
using LoupixDeck.PluginSdk;
using LoupixDeck.Utils;
using SkiaSharp;
using Device = LoupixDeck.LoupedeckDevice.Device.LoupedeckDevice;

namespace LoupixDeck.Controllers;

/// <summary>
/// Plugin-drawn wheel screen (issue #396): a command bound to the CT's wheel that implements
/// <see cref="IWheelRenderer"/>, or an exclusive provider overriding
/// <see cref="IExclusiveModeProvider.RenderWheel"/>, draws the 240×240 screen instead of the user's
/// wheel canvas or the automatic layout. A renderer with an <see cref="IWheelRenderer.Interval"/> is
/// repainted by the central animation scheduler through the wheel's own redraw gate.
///
/// Like the animated plugin strips (PluginStripAnimator.cs) this lives in the controller: the bound renderer is only
/// known at the moment <see cref="RedrawWheel"/> picks it, so every path that changes it (page,
/// mode, binding edit, plugin reload, takeover) already passes here and updates the schedule.
/// </summary>
public partial class LoupedeckLiveSController
{
    // Ceiling for an interval-driven wheel: every repaint is a full 240×240 push, the same bound the
    // animated wheel image layers use.
    private const int MaxPluginWheelFps = 30;

    // Interval of the renderer drawing the wheel right now, in ticks; 0 = none or on demand only.
    private long _pluginWheelIntervalTicks;

    // Scheduler-clock time of the last interval push (long intervals are gated against it).
    private TimeSpan _pluginWheelLastTick = TimeSpan.MinValue;

    // Stopwatch timestamp of the source's last tick, so a redraw can tell a running source (leave
    // its cadence alone) from a parked one (wake, screensaver end, takeover end: kick it).
    private long _pluginWheelLastTickTs;

    // True when the last wheel frame came from the exclusive provider: its EntriesChanged then
    // repaints the wheel. A provider that leaves the wheel alone does not cost a wheel push per event.
    private volatile bool _wheelDrawnByExclusive;
    private IExclusiveModeProvider _wheelExclusiveProvider;

    /// <summary>The interval-driven wheel's registration on the central scheduler.</summary>
    private sealed class PluginWheelAnimationSource(LoupedeckLiveSController owner)
        : Services.Animation.IAnimationSource
    {
        public int TargetFps => owner.PluginWheelTargetFps();
        public bool IsActive => owner.PluginWheelActive();
        public Task RenderFrameAsync(Services.Animation.AnimationRenderContext context)
            => owner.RenderPluginWheelFrameAsync(context);
    }

    private PluginWheelAnimationSource _pluginWheelAnimationSource;

    /// <summary>Registers the wheel renderer source on the central scheduler. Idempotent; called
    /// once during controller init.</summary>
    private void RegisterPluginWheelAnimationSource()
    {
        _pluginWheelAnimationSource ??= new PluginWheelAnimationSource(this);
        animationScheduler.Register(_pluginWheelAnimationSource);
    }

    /// <summary>Unregisters the wheel renderer source (controller shutdown).</summary>
    private void UnregisterPluginWheelAnimationSource()
    {
        if (_pluginWheelAnimationSource != null)
            animationScheduler.Unregister(_pluginWheelAnimationSource);
    }

    private TimeSpan PluginWheelInterval => TimeSpan.FromTicks(Interlocked.Read(ref _pluginWheelIntervalTicks));

    /// <summary>One tick per interval up to <see cref="MaxPluginWheelFps"/>; intervals of a second
    /// or more tick once a second and are gated in <see cref="RenderPluginWheelFrameAsync"/>.</summary>
    private int PluginWheelTargetFps()
    {
        TimeSpan interval = PluginWheelInterval;
        if (interval <= TimeSpan.Zero) return 1;
        return Math.Clamp((int)Math.Round(1000.0 / interval.TotalMilliseconds), 1, MaxPluginWheelFps);
    }

    /// <summary>Wants frames while a renderer with an interval draws the wheel and nothing else owns
    /// the display. The open mode menu is checked by <see cref="RefreshWheelAnimationFrame"/>.</summary>
    private bool PluginWheelActive()
    {
        if (_isDeviceOff || _screensaverActive || _fullDisplayActive) return false;
        return PluginWheelInterval > TimeSpan.Zero && deviceService.Device is { HasWheel: true };
    }

    private async Task RenderPluginWheelFrameAsync(Services.Animation.AnimationRenderContext context)
    {
        Interlocked.Exchange(ref _pluginWheelLastTickTs, Stopwatch.GetTimestamp());

        TimeSpan interval = PluginWheelInterval;
        if (interval <= TimeSpan.Zero || context.CancellationToken.IsCancellationRequested) return;

        // Below a second the tick rate already matches the interval. Above it the source ticks once
        // a second, so push only when the interval is (within half a tick) used up.
        if (interval >= TimeSpan.FromSeconds(1) && _pluginWheelLastTick != TimeSpan.MinValue &&
            context.Elapsed - _pluginWheelLastTick < interval - TimeSpan.FromMilliseconds(500))
            return;

        _pluginWheelLastTick = context.Elapsed;
        await RefreshWheelAnimationFrame().ConfigureAwait(false);
    }

    /// <summary>
    /// Records the interval of the renderer that just drew the wheel (zero when none did) and starts
    /// the scheduler on it. Called by every wheel redraw, so a page, mode or binding change, a wake
    /// or the end of a takeover restarts or stops the ticks without wiring of its own.
    /// </summary>
    private void SchedulePluginWheel(TimeSpan interval)
    {
        if (interval < TimeSpan.Zero) interval = TimeSpan.Zero;
        long previous = Interlocked.Exchange(ref _pluginWheelIntervalTicks, interval.Ticks);

        if (interval <= TimeSpan.Zero || _pluginWheelAnimationSource == null) return;

        // A running source keeps its cadence: pulling its next tick forward on every redraw (each
        // detent of a turn, the tick's own redraw) would double the pushes. Only a new renderer or a
        // source that stopped ticking (parked while the device was off or someone else owned the
        // display) is kicked.
        if (previous != interval.Ticks)
        {
            _pluginWheelLastTick = TimeSpan.MinValue;
        }
        else
        {
            TimeSpan sinceTick = Stopwatch.GetElapsedTime(Interlocked.Read(ref _pluginWheelLastTickTs));
            TimeSpan tickPeriod = TimeSpan.FromSeconds(1.0 / PluginWheelTargetFps());
            if (sinceTick < tickPeriod * 2 + TimeSpan.FromMilliseconds(250)) return;
        }

        animationScheduler.RequestFrame(_pluginWheelAnimationSource);
    }

    /// <summary>
    /// The active exclusive provider's wheel frame, or null when there is no takeover or the
    /// provider leaves the wheel to the page.
    /// </summary>
    private SKBitmap RenderExclusiveWheel(int size)
    {
        IExclusiveModeProvider provider = exclusiveMode.IsActive ? exclusiveMode.Current : null;
        SKBitmap frame = null;
        if (provider != null)
        {
            using (router.Enter(serviceProvider))
                frame = BitmapHelper.RenderWheelPlugin(size, provider.RenderWheel);
        }

        _wheelDrawnByExclusive = frame != null;
        return frame;
    }

    /// <summary>
    /// The frame of the first wheel binding that draws the wheel itself, in the same order the
    /// adjustment value is resolved in (<see cref="ResolveDialAdjustmentValue"/>), or null when none
    /// is bound or it declined. <paramref name="interval"/> is that renderer's repaint interval.
    /// </summary>
    private SKBitmap RenderBoundWheel(RotaryButton wheel, AdjustmentValue? value, int size, out TimeSpan interval)
    {
        interval = TimeSpan.Zero;
        string label = string.IsNullOrWhiteSpace(wheel.DisplayText) ? null : wheel.DisplayText;

        foreach (string command in new[] { wheel.RotaryLeftCommand, wheel.RotaryRightCommand, wheel.Command })
        {
            if (commandService.GetWheelRenderInterval(command) is not { } renderInterval) continue;

            // A renderer that declines still keeps its interval: it may draw on the next tick.
            interval = renderInterval;
            return BitmapHelper.RenderWheelPlugin(size, canvas =>
                commandService.TryRenderWheel(command, Device.WheelRotaryIndex, canvas, size, label, value));
        }

        return null;
    }

    /// <summary>
    /// Repaints the wheel for an exclusive-mode change: on enter and exit, when the provider
    /// changed, when <paramref name="takeoverChanged"/> reports a scope change (the wheel may
    /// follow the rotary scope), and on <see cref="IExclusiveModeProvider.EntriesChanged"/>
    /// while the provider draws it.
    /// </summary>
    private Task RedrawWheelForExclusive(bool takeoverChanged)
    {
        IExclusiveModeProvider provider = exclusiveMode.IsActive ? exclusiveMode.Current : null;
        bool providerChanged = !ReferenceEquals(provider, _wheelExclusiveProvider);
        _wheelExclusiveProvider = provider;

        return providerChanged || takeoverChanged || _wheelDrawnByExclusive ? RedrawWheel() : Task.CompletedTask;
    }
}
