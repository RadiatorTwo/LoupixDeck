using LoupixDeck.Models;
using LoupixDeck.PluginSdk;
using LoupixDeck.Utils;
using SkiaSharp;

namespace LoupixDeck.Controllers;

/// <summary>
/// Scheduler-driven animation for plugin side-strip sessions (issue #124). A session that also
/// implements <see cref="IAnimatedSideStripSession"/> is ticked by the central animation scheduler
/// at its declared rate instead of only redrawing when it raises
/// <see cref="ISideStripSession.StripChanged"/> — the strip counterpart of the animated button path.
///
/// This lives in the controller rather than in a service because everything a frame needs is
/// controller-private: the live sessions (<c>_stripSession</c>, created and disposed by
/// <c>EnsureStripAttachment</c>/<c>DetachStripAt</c>), the per-side redraw gate and generation
/// counters it shares with the swipe animator and the StripChanged redraw, the drag-busy guard, the
/// gated bitmap dispose queue, and the <c>PushStrip</c> tail. Driving it from outside would mean
/// handing plugin session objects across a service boundary (a second root on the plugin's
/// collectible load context) and duplicating the coalescing contract the other two paths rely on.
/// <see cref="Services.Animation.SideDisplayAnimationSource"/> stays what it is: the animated
/// *image layer* source for free-draw strips.
/// </summary>
public partial class LoupedeckLiveSController
{
    // The scheduler's own hard ceiling. A strip can ask for more than the 30 fps default, but not
    // beyond what the scheduler will ever drive.
    private const int MaxPluginStripFps = 120;

    // Strip geometry, matching the SideStripContext the session was created with
    // (the height is the shared StripHeight from the swipe animator).
    private const int StripWidth = 60;

    // Per side (0 = Left): the animated capability of the attached strip session, or null when the
    // session is plain event-driven (or there is none).
    private readonly IAnimatedSideStripSession[] _animatedStripSession = new IAnimatedSideStripSession[2];
    private readonly long[] _animatedStripFrameCounter = [0, 0];
    private readonly long[] _animatedStripLastPushed = [-1, -1];
    private readonly TimeSpan[] _animatedStripStartElapsed = [TimeSpan.Zero, TimeSpan.Zero];
    private readonly bool[] _animatedStripFinished = [false, false];

    // Global FPS ceiling we raised for a fast strip, and the value to restore afterwards
    // (0 = we haven't raised it).
    private int _stripPreviousFpsLimit;

    /// <summary>The animated plugin strips' registration on the central scheduler. One per-device
    /// source drives both columns; it goes inactive whenever something else owns the strips, so the
    /// scheduler stops ticking (and can park the loop) without unregistering.</summary>
    private sealed class PluginStripAnimationSource(LoupedeckLiveSController owner)
        : Services.Animation.IAnimationSource
    {
        public int TargetFps => owner.PluginStripTargetFps();
        public bool IsActive => owner.PluginStripActive();
        public Task RenderFrameAsync(Services.Animation.AnimationRenderContext context)
            => owner.RenderPluginStripFramesAsync(context);
    }

    private PluginStripAnimationSource _pluginStripAnimationSource;

    /// <summary>Registers the animated-strip source on the central scheduler. Idempotent; called
    /// once during controller init.</summary>
    private void RegisterPluginStripAnimationSource()
    {
        _pluginStripAnimationSource ??= new PluginStripAnimationSource(this);
        animationScheduler.Register(_pluginStripAnimationSource);
    }

    /// <summary>Unregisters the animated-strip source (controller shutdown).</summary>
    private void UnregisterPluginStripAnimationSource()
    {
        if (_pluginStripAnimationSource != null)
            animationScheduler.Unregister(_pluginStripAnimationSource);
    }

    /// <summary>Reads a session's declared rate defensively — a throwing plugin property must not
    /// kill the tick. Returns 0 ("use the host default") on anything unusable.</summary>
    private static int ReadStripTargetFps(IAnimatedSideStripSession session)
    {
        try
        {
            var fps = session.TargetFps;
            return fps <= 0 ? 0 : Math.Clamp(fps, 1, MaxPluginStripFps);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Animated side-strip TargetFps threw: {ex.Message}");
            return 0;
        }
    }

    /// <summary>Highest rate the currently animating strips ask for; 0 means "global limit".</summary>
    private int PluginStripTargetFps()
    {
        var fps = 0;
        for (var idx = 0; idx < 2; idx++)
        {
            var session = _animatedStripSession[idx];
            if (session == null || _animatedStripFinished[idx]) continue;
            fps = Math.Max(fps, ReadStripTargetFps(session));
        }
        return fps;
    }

    /// <summary>Whether any strip currently wants frames. Mirrors the veto set of
    /// <c>RedrawStripCoalesced</c> so the animation yields to every display owner, plus the
    /// drag-busy guard so it never fights a swipe.</summary>
    private bool PluginStripActive()
    {
        if (_isDeviceOff || folderNav.IsActive || _screensaverActive || _fullDisplayActive) return false;
        if (exclusiveMode.Owns(ExclusiveControlScope.SideDisplays)) return false;

        for (var idx = 0; idx < 2; idx++)
        {
            if (_animatedStripSession[idx] == null || _animatedStripFinished[idx]) continue;
            if (IsStripDragBusy(idx)) continue;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Records a freshly attached session's animation capability and resets its frame state. Called
    /// from <c>EnsureStripAttachment</c> right after the session is stored; a session that doesn't
    /// implement <see cref="IAnimatedSideStripSession"/> simply leaves the slot empty and keeps the
    /// existing event-driven behavior.
    /// </summary>
    private void AttachAnimatedStripSession(int idx, ISideStripSession session)
    {
        _animatedStripSession[idx] = session as IAnimatedSideStripSession;
        _animatedStripFrameCounter[idx] = 0;
        _animatedStripLastPushed[idx] = -1;
        _animatedStripStartElapsed[idx] = TimeSpan.MinValue; // stamped on the first frame
        _animatedStripFinished[idx] = false;

        if (_animatedStripSession[idx] == null) return;

        SyncStripFpsLimit();
        if (_pluginStripAnimationSource != null)
            animationScheduler.RequestFrame(_pluginStripAnimationSource);
    }

    /// <summary>Clears a side's animation slot. Called from <c>DetachStripAt</c> BEFORE the session
    /// is disposed, so no new frame can start on a session that is being torn down.</summary>
    private void DetachAnimatedStripSession(int idx)
    {
        if (_animatedStripSession[idx] == null) return;

        _animatedStripSession[idx] = null;
        _animatedStripFinished[idx] = false;
        SyncStripFpsLimit();
    }

    /// <summary>
    /// Raises the scheduler's global FPS ceiling while a strip asks for more than the current limit,
    /// and restores it once no animated strip is attached any more. Raising only lifts the ceiling —
    /// each source is still driven at its own TargetFps — but note that sources declaring 0 follow
    /// the ceiling and therefore tick faster while it is raised.
    /// </summary>
    private void SyncStripFpsLimit()
    {
        var wanted = PluginStripTargetFps();

        if (wanted <= 0)
        {
            // No animated strip left (or none asks for a specific rate): give the ceiling back.
            if (_stripPreviousFpsLimit > 0)
            {
                try { animationScheduler.SetGlobalFpsLimit(_stripPreviousFpsLimit); }
                catch { /* best effort */ }
                _stripPreviousFpsLimit = 0;
            }
            return;
        }

        if (wanted <= animationScheduler.GlobalFpsLimit) return;

        // Remember the pre-raise ceiling only once, so two consecutive raises don't record
        // our own raised value as the thing to restore.
        if (_stripPreviousFpsLimit == 0)
            _stripPreviousFpsLimit = animationScheduler.GlobalFpsLimit;

        try { animationScheduler.SetGlobalFpsLimit(wanted); }
        catch { /* best effort */ }
    }

    /// <summary>Per-side push floor. Defaults to the shared <c>StripMinRedrawMs</c> (~30 fps); a
    /// strip that asks for more gets a floor derived from its own rate. Only this path uses the
    /// derived value — the swipe animator and the StripChanged redraw keep the 33 ms floor.</summary>
    private int StripMinRedrawMsFor(int idx)
    {
        var session = _animatedStripSession[idx];
        if (session == null) return StripMinRedrawMs;

        var fps = ReadStripTargetFps(session);
        if (fps <= 0) return StripMinRedrawMs;

        var floor = 1000 / fps;
        return Math.Max(1, Math.Min(StripMinRedrawMs, floor));
    }

    /// <summary>Ticks every animating side. The two sides own independent gates, so they render and
    /// push concurrently instead of halving each other's rate.</summary>
    private Task RenderPluginStripFramesAsync(Services.Animation.AnimationRenderContext context)
    {
        var left = _animatedStripSession[0] != null && !_animatedStripFinished[0]
            ? PushPluginStripFrame(RotarySide.Left, 0, context)
            : Task.CompletedTask;

        var right = _animatedStripSession[1] != null && !_animatedStripFinished[1]
            ? PushPluginStripFrame(RotarySide.Right, 1, context)
            : Task.CompletedTask;

        return Task.WhenAll(left, right);
    }

    /// <summary>
    /// Renders and pushes one animation frame for a side. Shares the per-side gate, generation
    /// counters and rate floor with the swipe animator and the provider redraw, so the three never
    /// race. Skips the device push when the plugin reports no new frame, so an idle animation costs
    /// no serial I/O.
    /// </summary>
    private async Task PushPluginStripFrame(RotarySide side, int idx,
        Services.Animation.AnimationRenderContext context)
    {
        var requested = Interlocked.Increment(ref _stripRedrawGen[idx]);
        await _stripRedrawGate[idx].WaitAsync();
        try
        {
            DrainStripDisposeQueue(idx);

            if (Interlocked.Read(ref _stripDrawnGen[idx]) >= requested) return;
            if (_isDeviceOff || folderNav.IsActive || exclusiveMode.Owns(ExclusiveControlScope.SideDisplays) ||
                _screensaverActive || _fullDisplayActive) return;
            // The drag/settle owns the strip until it finishes; the animation resumes after it.
            if (IsStripDragBusy(idx)) return;

            var floor = StripMinRedrawMsFor(idx);
            var since = Environment.TickCount64 - _stripLastDrawTick[idx];
            if (since < floor)
                await Task.Delay((int)(floor - since));

            // Re-read after the awaits — a detach may have run in the meantime.
            var session = _animatedStripSession[idx];
            if (session == null || _animatedStripFinished[idx]) return;

            if (_animatedStripStartElapsed[idx] == TimeSpan.MinValue)
                _animatedStripStartElapsed[idx] = context.Elapsed;

            var frame = new AnimationFrameContext
            {
                FrameNumber = _animatedStripFrameCounter[idx]++,
                Elapsed = context.Elapsed - _animatedStripStartElapsed[idx],
                Delta = context.Delta,
                EffectiveFps = context.EffectiveFps
            };

            var snapshot = Interlocked.Read(ref _stripRedrawGen[idx]);
            var bitmap = new SKBitmap(StripWidth, StripHeight);
            AnimationFrameInfo info;

            // Plugin code may call back into the host, so give it the right ambient device; all
            // Skia work stays under the shared gate (SkiaRenderCanvas does not lock itself).
            using (router.Enter(serviceProvider))
            {
                lock (SkiaRenderGate.Sync)
                {
                    using var canvas = new SKCanvas(bitmap);
                    var renderCanvas = new SkiaRenderCanvas(canvas, StripWidth, StripHeight);
                    info = session.RenderStripFrame(renderCanvas, frame);
                    if (info.Drawn) canvas.Flush();
                }
            }

            if (info.IsFinal)
                _animatedStripFinished[idx] = true;

            // Dirty-check on the plugin's frame number, exactly like the animated button path.
            if (!info.Drawn || info.FrameNumber == _animatedStripLastPushed[idx])
            {
                bitmap.Dispose();
                return;
            }

            // Not disposed after the push — TouchButton.RenderedImage owns the bitmap's lifetime
            // (deferred dispose), same as DrawSideStrip and the swipe animator's frames.
            await PushStrip(side, bitmap);

            _animatedStripLastPushed[idx] = info.FrameNumber;
            Interlocked.Exchange(ref _stripDrawnGen[idx], snapshot);
            _stripLastDrawTick[idx] = Environment.TickCount64;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Animated side-strip frame failed ({side}): {ex.Message}");
        }
        finally
        {
            _stripRedrawGate[idx].Release();
        }
    }
}
