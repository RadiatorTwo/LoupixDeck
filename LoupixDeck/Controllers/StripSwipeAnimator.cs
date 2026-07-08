using System.Diagnostics;
using LoupixDeck.LoupedeckDevice;
using LoupixDeck.Models;
using LoupixDeck.Models.Extensions;
using LoupixDeck.PluginSdk;
using LoupixDeck.Utils;
using SkiaSharp;

namespace LoupixDeck.Controllers;

/// <summary>
/// Finger-following swipe animation for the Razer side strips (segmented / free-draw
/// pages). The device streams the finger position during a drag as a run of TOUCH
/// packets; this part of the controller turns that stream into a vertical slide where
/// the current page tracks the finger and the adjacent page follows it in. On release
/// the gesture commits distance-based (past ~half the strip → page change, otherwise it
/// glides back). Plugin-override strips are excluded — they own their own pixels and
/// gestures. Falls back to a clean release-time slide when the device sends no
/// intermediate packets, since the commit offset is derived from start/end anyway.
///
/// Bitmap lifetime: the three cached page bitmaps are disposed only while holding the
/// per-side redraw gate (via <see cref="_stripDisposeQueue"/>), and a cached reference
/// is always replaced with the fresh bitmap before the old one is queued. That keeps a
/// concurrent gated render from ever reading a just-disposed SKBitmap — see the
/// access-violation history around Skia object disposal.
/// </summary>
public partial class LoupedeckLiveSController
{
    private const int StripHeight = 270;
    // Commit when the swipe passed half the strip; below that it snaps back...
    private const int StripCommitThreshold = StripHeight / 2;
    // ...unless it was a fast flick: a release moving at least this fast (px/ms) in the
    // travel direction commits even on a short swipe — the small panel makes a pure
    // distance threshold feel like it needs the whole screen.
    private const double StripFlickVelocity = 0.35;
    // A flick still needs a little travel so a stationary tap can't trigger it.
    private const int StripFlickMinTravel = 10;
    // A release under this much total travel (and with no animated movement) is a tap.
    private const int StripTapMaxMove = 8;
    private const int StripSettleMs = 150;

    private sealed class StripDragState
    {
        public bool Active;
        public byte TouchId;
        public int StartY;
        public int Offset;       // last pushed visual offset (px); sign drives the neighbour
        public bool Moved;       // any travel beyond the tap threshold was seen
        public double StartMs;   // high-res timestamp of the first sample
        public double LastMs;    // timestamp of the last sample (for velocity)
        public int LastY;        // y of the last sample
        public double Velocity;  // smoothed signed px/ms (negative = upward)
        public SKBitmap Current; // pre-rendered current page
        public SKBitmap Next;    // pre-rendered page below (paged to on an up-swipe)
        public SKBitmap Prev;    // pre-rendered page above (paged to on a down-swipe)
    }

    private readonly StripDragState[] _drag = [new(), new()];

    /// <summary>A time-based slide of one side strip toward a target offset, driven by the
    /// central <see cref="Services.Animation.IAnimationScheduler"/> (issue #119) rather than a
    /// private timer. Started on a swipe release (commit or snap-back) and on command/GUI-driven
    /// paging; the scheduler ticks <see cref="StripAnimationSource"/> which advances the offset
    /// from <see cref="FromOffset"/> to <see cref="Target"/> over <see cref="StripSettleMs"/> with
    /// a cubic ease-out, then commits (or restores) the page on the final frame.</summary>
    private sealed class StripTransition
    {
        public bool Active;
        public int FromOffset;
        public int Target;      // 0 for snap-back, ±StripHeight for a commit
        public bool Commit;     // true = change the page, false = restore the live strip
        public int Direction;   // sign drives the default Next/Previous commit
        public Action CommitAction; // explicit commit (goto lands on an arbitrary index)
        public long StartTimestamp; // Stopwatch timestamp of the transition's first frame
    }

    private readonly StripTransition[] _transition = [new(), new()];

    /// <summary>The side-strip transition's registration on the central animation scheduler.
    /// A single per-device source drives both columns; it is active only while a transition is
    /// in flight, so the scheduler stops ticking it (and can park the loop) once both strips
    /// have settled. Nested so it can reach the controller's strip render/push internals.</summary>
    private sealed class StripAnimationSource(LoupedeckLiveSController owner) : Services.Animation.IAnimationSource
    {
        // ~30 fps matches the historical StripMinRedrawMs floor and the strip push rate limit.
        public int TargetFps => 30;
        public bool IsActive => owner._transition[0].Active || owner._transition[1].Active;
        public Task RenderFrameAsync(Services.Animation.AnimationRenderContext context)
            => owner.RenderStripTransitionsAsync();
    }

    private StripAnimationSource _stripAnimationSource;

    /// <summary>Lightweight gesture tracking for strips that do NOT run the finger-follow
    /// animation (plugin-override, and segmented pages with a single rotary page so there's
    /// nothing to slide to). Without this, a tap fired immediately on TOUCH_START, so a swipe
    /// — which also starts with a TOUCH_START — wrongly triggered the strip command (e.g. an
    /// audio segment muting on a swipe). Here the tap is deferred to release and suppressed
    /// when the finger moved, so only a genuine tap runs the command.</summary>
    private sealed class StripTapState
    {
        public bool Active;
        public byte TouchId;
        public int StartY;
        public bool Moved;
    }

    private readonly StripTapState[] _tapTrack = [new(), new()];

    // Bitmaps awaiting disposal, freed only under _stripRedrawGate[idx].
    private readonly List<SKBitmap>[] _stripDisposeQueue = [new(), new()];

    /// <summary>True while a side's strip is mid-drag or mid-settle — used to suppress
    /// provider-driven full redraws that would fight the animation.</summary>
    private bool IsStripDragBusy(int idx) => _drag[idx].Active || _transition[idx].Active;

    /// <summary>Registers the side-strip transition source on the central scheduler. Idempotent;
    /// called once during controller init. No-op on devices whose strips aren't driven here.</summary>
    private void RegisterStripAnimationSource()
    {
        _stripAnimationSource ??= new StripAnimationSource(this);
        animationScheduler.Register(_stripAnimationSource);
    }

    /// <summary>Unregisters the transition source (controller shutdown).</summary>
    private void UnregisterStripAnimationSource()
    {
        if (_stripAnimationSource != null)
            animationScheduler.Unregister(_stripAnimationSource);
    }

    /// <summary>True when the side's current page supports the finger-follow animation:
    /// a side-strip device, not off/exclusive/folder, a non-plugin page, and more than one
    /// page to slide between. This gates the swipe finger-follow, which always animates; the
    /// RotaryPageTransitionAnimationEnabled setting only affects the command/GUI paths.</summary>
    private bool StripAnimationApplicable(RotarySide side)
    {
        if (deviceService.Device?.HasSideStrips != true) return false;
        if (_isDeviceOff || folderNav.IsActive || exclusiveMode.IsActive) return false;
        var page = pageManager.GetCurrentRotaryPage(side);
        if (page == null || page.StripMode == StripMode.PluginOverride) return false;
        return pageManager.GetRotaryPages(side).Count > 1;
    }

    /// <summary>Feeds one touch sample (start or move) into the drag for a side strip.</summary>
    private void OnStripTouchSample(RotarySide side, int idx, int y, byte touchId)
    {
        var st = _drag[idx];
        // A new touch id (or no active drag) starts a fresh drag. Callers feed only the
        // live (changed) touch here, so a different id means the previous finger is gone
        // — possibly because its TOUCH_END frame was lost — and this is a new gesture.
        if (!st.Active || st.TouchId != touchId)
        {
            BeginStripDrag(side, idx, touchId, y);
            return;
        }

        var dy = Math.Clamp(y - st.StartY, -StripHeight, StripHeight);
        st.Offset = dy;
        if (Math.Abs(dy) > StripTapMaxMove) st.Moved = true;
        UpdateVelocity(st, y, NowMs());
        _ = PushAnimationFrame(side, idx);
    }

    private void BeginStripDrag(RotarySide side, int idx, byte touchId, int y)
    {
        var st = _drag[idx];
        CancelStripTransition(idx);

        PrepareStripBitmaps(side, idx);

        var nowMs = NowMs();
        st.StartY = y;
        st.Offset = 0;
        st.Moved = false;
        st.StartMs = nowMs;
        st.LastMs = nowMs;
        st.LastY = y;
        st.Velocity = 0;
        st.TouchId = touchId;
        st.Active = true;
    }

    /// <summary>Renders the side's current/next/prev rotary page bitmaps into the drag
    /// state and retires the previous ones through the gated dispose queue. Shared by the
    /// finger-follow <see cref="BeginStripDrag"/> and the command/GUI-driven animate path.
    /// The fresh bitmaps are assigned before the old ones are queued, so any gated render
    /// in flight reads the new references, never a queued-for-dispose bitmap.</summary>
    private void PrepareStripBitmaps(RotarySide side, int idx)
    {
        var st = _drag[idx];
        var oldC = st.Current;
        var oldN = st.Next;
        var oldP = st.Prev;

        var current = pageManager.GetCurrentRotaryPage(side);
        var next = pageManager.PeekRotaryPage(side, +1);
        var prev = pageManager.PeekRotaryPage(side, -1);

        st.Current = RenderStripFor(current, side, useSessions: true);
        st.Next = next != null ? RenderStripFor(next, side, useSessions: false) : null;
        st.Prev = prev != null ? RenderStripFor(prev, side, useSessions: false) : null;

        EnqueueDispose(idx, oldC, oldN, oldP);
    }

    /// <summary>Like <see cref="PrepareStripBitmaps"/> but for a goto-by-index jump: renders
    /// the <paramref name="targetIndex"/> page into the travel-side neighbour slot (Next for
    /// an up-slide, Prev for a down-slide) and nulls the other, so the slide reveals the exact
    /// target page even when it is not adjacent. The commit lands directly on the target.</summary>
    private void PrepareGotoBitmaps(RotarySide side, int idx, int targetIndex, int direction)
    {
        var st = _drag[idx];
        var oldC = st.Current;
        var oldN = st.Next;
        var oldP = st.Prev;

        var current = pageManager.GetCurrentRotaryPage(side);
        var target = pageManager.GetRotaryPages(side)[targetIndex];

        st.Current = RenderStripFor(current, side, useSessions: true);
        var incoming = RenderStripFor(target, side, useSessions: false);
        if (direction < 0)
        {
            st.Next = incoming;
            st.Prev = null;
        }
        else
        {
            st.Prev = incoming;
            st.Next = null;
        }

        EnqueueDispose(idx, oldC, oldN, oldP);
    }

    /// <summary>Builds the composite for the side's current visual offset, picking the
    /// neighbour by the offset's sign. Returns null if the drag was torn down.</summary>
    private SKBitmap BuildDragComposite(int idx)
    {
        var st = _drag[idx];
        var current = st.Current;
        if (current == null) return null;
        var offset = st.Offset;
        var neighbor = offset < 0 ? st.Next : offset > 0 ? st.Prev : null;
        return BitmapHelper.ComposeVerticalSlide(current, neighbor, offset);
    }

    /// <summary>Coalesced, rate-limited push of the current drag composite. Shares the
    /// per-side gate and generation counters with the provider redraw path so the two
    /// never race, and always renders the newest offset (like a provider reads live
    /// state).</summary>
    private async Task PushAnimationFrame(RotarySide side, int idx)
    {
        var requested = Interlocked.Increment(ref _stripRedrawGen[idx]);
        await _stripRedrawGate[idx].WaitAsync();
        try
        {
            DrainStripDisposeQueue(idx);

            if (Interlocked.Read(ref _stripDrawnGen[idx]) >= requested) return;
            if (_isDeviceOff || folderNav.IsActive || exclusiveMode.IsActive) return;

            var since = Environment.TickCount64 - _stripLastDrawTick[idx];
            if (since < StripMinRedrawMs)
                await Task.Delay((int)(StripMinRedrawMs - since));

            var snapshot = Interlocked.Read(ref _stripRedrawGen[idx]);
            var bmp = BuildDragComposite(idx);
            if (bmp != null)
                await PushStrip(side, bmp);
            Interlocked.Exchange(ref _stripDrawnGen[idx], snapshot);
            _stripLastDrawTick[idx] = Environment.TickCount64;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Side-strip animation frame failed ({side}): {ex.Message}");
        }
        finally
        {
            _stripRedrawGate[idx].Release();
        }
    }

    /// <summary>Mirrors a strip bitmap to the on-screen slot and pushes it to the
    /// device — the push tail shared with <c>DrawSideStrip</c>.</summary>
    private async Task PushStrip(RotarySide side, SKBitmap strip)
    {
        if (deviceService.Device is not LoupedeckDevice.Device.RazerStreamControllerDevice razer)
            return;

        var slotIndex = side == RotarySide.Left
            ? LoupedeckDevice.Device.RazerStreamControllerDevice.LeftSideIndex
            : LoupedeckDevice.Device.RazerStreamControllerDevice.RightSideIndex;

        var slotButton = config.CurrentTouchButtonPage?.TouchButtons?.FindByIndex(slotIndex);
        slotButton?.RenderedImage = strip;

        await razer.DrawTouchSlot(slotIndex, strip);
    }

    /// <summary>Handles a finger release for any active strip drag: decides commit vs
    /// snap-back (distance-based) and runs the settle, or routes a tap when the finger
    /// barely moved. Returns true when it consumed the release.</summary>
    private bool HandleStripDragEnd(TouchInfo changedTouch)
    {
        if (changedTouch == null) return false;

        for (var idx = 0; idx < 2; idx++)
        {
            var st = _drag[idx];
            if (!st.Active || st.TouchId != changedTouch.Id) continue;

            st.Active = false;
            var side = idx == 0 ? RotarySide.Left : RotarySide.Right;
            var endDy = Math.Clamp(changedTouch.Y - st.StartY, -StripHeight, StripHeight);

            // A barely-moved release is a tap, not a swipe: route it like the legacy
            // tap path (segment sessions consume it; free-draw is a no-op).
            if (!st.Moved && Math.Abs(endDy) < StripTapMaxMove)
            {
                RouteStripTap(side, idx, changedTouch);
                // Cached bitmaps are retired by the next BeginStripDrag (or ResetStripDrags),
                // never here — a release can't clobber a freshly-started drag's bitmaps.
                return true;
            }

            // Fold the release segment into the velocity estimate.
            UpdateVelocity(st, changedTouch.Y, NowMs());

            var travelDir = endDy < 0 ? -1 : 1;
            // Flick: a fast release in the same direction as the net travel commits even on
            // a short swipe. Direction == travel direction, so the slide never flashes the
            // wrong neighbour.
            var flick = Math.Abs(st.Velocity) >= StripFlickVelocity
                        && Math.Abs(endDy) >= StripFlickMinTravel
                        && (st.Velocity < 0 ? travelDir < 0 : travelDir > 0);

            var direction = travelDir;
            var hasNeighbor = direction < 0 ? st.Next != null : st.Prev != null;
            var commit = hasNeighbor && (flick || Math.Abs(endDy) >= StripCommitThreshold);
            BeginStripTransition(idx, st.Offset, commit, direction);
            return true;
        }

        return false;
    }

    /// <summary>Arms a side-strip transition and hands the pacing to the central animation
    /// scheduler (issue #119): it slides from <paramref name="fromOffset"/> to the target
    /// (0 for snap-back, ±height for a commit) over <see cref="StripSettleMs"/>, then on the
    /// final frame commits the page (<paramref name="commit"/>) or restores the live strip.
    /// <paramref name="commitAction"/> overrides the default one-step Next/Previous change so
    /// the goto path can land on an arbitrary index. Starting a new transition supersedes any
    /// in-flight one for the same side.</summary>
    private void BeginStripTransition(int idx, int fromOffset, bool commit, int direction, Action commitAction = null)
    {
        var tr = _transition[idx];
        tr.FromOffset = fromOffset;
        tr.Target = !commit ? 0 : (direction < 0 ? -StripHeight : StripHeight);
        tr.Commit = commit;
        tr.Direction = direction;
        tr.CommitAction = commitAction;
        tr.StartTimestamp = Stopwatch.GetTimestamp();
        tr.Active = true;

        // Ensure the source is registered (init normally does this) and wake the loop so it
        // starts ticking the now-active transition on its next pass.
        _stripAnimationSource ??= new StripAnimationSource(this);
        animationScheduler.RequestFrame(_stripAnimationSource);
    }

    /// <summary>One scheduler frame for the side-strip transitions: advances each active side's
    /// offset by wall-clock progress (cubic ease-out), pushes the composite, and on completion
    /// commits the page or restores the live strip. Runs on a scheduler background thread; the
    /// page commit is marshalled to the UI thread. The scheduler serialises frames for this
    /// source, so the two sides are advanced sequentially without overlapping pushes.</summary>
    private async Task RenderStripTransitionsAsync()
    {
        for (var idx = 0; idx < 2; idx++)
        {
            var tr = _transition[idx];
            if (!tr.Active) continue;

            var side = idx == 0 ? RotarySide.Left : RotarySide.Right;
            try
            {
                var elapsedMs = Stopwatch.GetElapsedTime(tr.StartTimestamp).TotalMilliseconds;
                var t = Math.Clamp(elapsedMs / StripSettleMs, 0.0, 1.0);
                var eased = 1 - Math.Pow(1 - t, 3); // cubic ease-out
                _drag[idx].Offset = (int)Math.Round(tr.FromOffset + ((tr.Target - tr.FromOffset) * eased));
                await PushAnimationFrame(side, idx);

                if (t < 1.0) continue;

                // Final frame: snap to the exact target and finish.
                _drag[idx].Offset = tr.Target;
                tr.Active = false;

                if (tr.Commit)
                {
                    // The neighbour is now fully shown; committing makes it the current page and
                    // OnRotaryPageChanged → DrawSideStrip redraws identical pixels at offset 0.
                    // The page change mutates bound (ObservableProperty/ObservableCollection)
                    // state, so marshal it to the UI thread (mirrors the GUI paging path).
                    var commitAction = tr.CommitAction;
                    var direction = tr.Direction;
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (commitAction != null)
                        {
                            commitAction();
                        }
                        else if (direction < 0)
                        {
                            pageManager.NextRotaryPage(side);
                        }
                        else
                        {
                            pageManager.PreviousRotaryPage(side);
                        }
                    });
                }
                else
                {
                    // Restore the authoritative live strip (resumes segment updates) at 0.
                    await RedrawStripCoalesced(side, idx);
                }
            }
            catch (Exception ex)
            {
                tr.Active = false;
                Console.WriteLine($"Side-strip transition failed ({side}): {ex.Message}");
            }
        }
    }

    // --- Command / GUI-driven paging (no finger) ---------------------------------------
    // Non-swipe rotary page changes reuse the same scheduler-driven transition as the swipe
    // release, so they slide identically. RenderStripTransitionsAsync owns the page commit,
    // so these paths must NOT also call PageManager (that would double-page). When the strip
    // animation doesn't apply the page still changes instantly, so commands work when the
    // animation is unavailable.

    /// <summary>True when the active device's side strips are actually rendered by this
    /// animator (the Razer Stream Controller — its strips live in the unified center buffer
    /// that <see cref="PushStrip"/> writes). Other side-strip devices (e.g. the CT, which
    /// uses separate left/right framebuffers) aren't driven here, so command/GUI paging
    /// changes their pages instantly instead of running a settle whose frames go nowhere.</summary>
    private bool StripAnimatorDrivesDevice
        => deviceService.Device is LoupedeckDevice.Device.RazerStreamControllerDevice;

    /// <summary>Pages a single side's rotary column, animating when the strip animation
    /// applies, otherwise changing the page instantly. <paramref name="direction"/> is
    /// -1 = next, +1 = previous (matching the swipe convention).</summary>
    private void PageRotarySideAnimated(RotarySide side, int direction)
    {
        var idx = SideIndex(side);

        // Setting off, device strips not driven here, not animatable, or a live finger drag
        // owns the strip → change instantly. (The setting only affects this command/GUI path;
        // the swipe finger-follow always animates.)
        if (!config.RotaryPageTransitionAnimationEnabled || !StripAnimatorDrivesDevice
            || !StripAnimationApplicable(side) || _drag[idx].Active)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (direction < 0) pageManager.NextRotaryPage(side);
                else pageManager.PreviousRotaryPage(side);
            });
            return;
        }

        // Render the bitmaps off the caller's thread (a GUI caller is on the UI thread and the
        // 3× strip render shouldn't block it). Arming the transition is cheap; the scheduler
        // then paces the frames on its own background thread.
        _ = Task.Run(() => AnimateRotaryStep(side, idx, direction));
    }

    /// <summary>Renders the current/neighbour bitmaps, then arms a full-commit transition in
    /// <paramref name="direction"/>. The scheduler drives the slide and commits the page.</summary>
    private void AnimateRotaryStep(RotarySide side, int idx, int direction)
    {
        CancelStripTransition(idx);   // supersede an in-flight transition (rapid double-press)
        PrepareStripBitmaps(side, idx);
        _drag[idx].Offset = 0;
        BeginStripTransition(idx, 0, commit: true, direction);
    }

    /// <summary>Renders the target page into the travel-side slot, then arms a transition that
    /// slides once in <paramref name="direction"/> and commits straight to
    /// <paramref name="targetIndex"/> (may be non-adjacent).</summary>
    private void AnimateRotaryGoto(RotarySide side, int idx, int targetIndex, int direction)
    {
        CancelStripTransition(idx);
        PrepareGotoBitmaps(side, idx, targetIndex, direction);
        _drag[idx].Offset = 0;
        BeginStripTransition(idx, 0, commit: true, direction,
            commitAction: () => pageManager.ApplyRotaryPage(side, targetIndex));
    }

    /// <summary>Animates a goto-by-index for one side using the shortest wrap direction
    /// (default forward/next on a tie). Falls back to an instant page change when the strip
    /// animation doesn't apply or a finger drag owns the strip.</summary>
    private void GotoRotarySideAnimated(RotarySide side, int targetIndex)
    {
        var idx = SideIndex(side);
        var pages = pageManager.GetRotaryPages(side);
        var count = pages.Count;
        if (targetIndex < 0 || targetIndex >= count) return;

        var from = pageManager.GetCurrentRotaryPageIndex(side);
        if (from == targetIndex) return;

        if (!config.RotaryPageTransitionAnimationEnabled || !StripAnimatorDrivesDevice
            || !StripAnimationApplicable(side) || _drag[idx].Active)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => pageManager.ApplyRotaryPage(side, targetIndex));
            return;
        }

        var forward = (((targetIndex - from) % count) + count) % count;
        var direction = forward <= count - forward ? -1 : +1;   // tie → forward (next)
        // Off the caller's thread (see PageRotarySideAnimated).
        _ = Task.Run(() => AnimateRotaryGoto(side, idx, targetIndex, direction));
    }

    /// <summary>Animated global "next rotary page". On the Razer Stream Controller both
    /// columns slide concurrently; on any other device the page changes instantly.</summary>
    public void AnimateNextRotaryPage()
    {
        if (!StripAnimatorDrivesDevice)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => pageManager.NextRotaryPage());
            return;
        }

        PageRotarySideAnimated(RotarySide.Left, -1);
        PageRotarySideAnimated(RotarySide.Right, -1);
    }

    /// <summary>Animated global "previous rotary page". On the Razer Stream Controller both
    /// columns slide concurrently; on any other device the page changes instantly.</summary>
    public void AnimatePreviousRotaryPage()
    {
        if (!StripAnimatorDrivesDevice)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => pageManager.PreviousRotaryPage());
            return;
        }

        PageRotarySideAnimated(RotarySide.Left, +1);
        PageRotarySideAnimated(RotarySide.Right, +1);
    }

    /// <summary>Animated per-side rotary paging (on-screen GUI side buttons).</summary>
    public void AnimateRotaryPageForSide(RotarySide side, bool next)
        => PageRotarySideAnimated(side, next ? -1 : +1);

    /// <summary>Animated goto-by-index. On the Razer Stream Controller each column animates
    /// toward <paramref name="pageIndex"/> in its own shortest direction; on any other device
    /// the page changes instantly (shared list).</summary>
    public void AnimateGotoRotaryPage(int pageIndex)
    {
        if (!StripAnimatorDrivesDevice)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => pageManager.ApplyRotaryPage(pageIndex));
            return;
        }

        GotoRotarySideAnimated(RotarySide.Left, pageIndex);
        GotoRotarySideAnimated(RotarySide.Right, pageIndex);
    }

    /// <summary>Feeds one touch sample (start or mid-drag) into the tap tracker for a strip
    /// that doesn't animate. Records the start position and flags any travel past the tap
    /// threshold so the release can tell a tap from a swipe.</summary>
    private void TrackStripTapSample(int idx, int y, byte touchId)
    {
        var st = _tapTrack[idx];
        if (!st.Active || st.TouchId != touchId)
        {
            st.Active = true;
            st.TouchId = touchId;
            st.StartY = y;
            st.Moved = false;
            return;
        }

        if (Math.Abs(y - st.StartY) > StripTapMaxMove)
            st.Moved = true;
    }

    /// <summary>Handles the release of a tracked (non-animated) strip gesture: routes the tap
    /// to the owning session only when the finger barely moved, so a swipe doesn't trigger the
    /// strip command. Paging on a swipe is owned by the page/plugin (via the device swipe
    /// event), so a suppressed swipe here is intentional. Returns true when it consumed the
    /// release.</summary>
    private bool HandleStripTapEnd(TouchInfo changedTouch)
    {
        if (changedTouch == null) return false;

        for (var idx = 0; idx < 2; idx++)
        {
            var st = _tapTrack[idx];
            if (!st.Active || st.TouchId != changedTouch.Id) continue;

            st.Active = false;
            var side = idx == 0 ? RotarySide.Left : RotarySide.Right;
            var endDy = changedTouch.Y - st.StartY;

            // Moved beyond the tap threshold → it was a swipe; suppress the command.
            if (st.Moved || Math.Abs(endDy) >= StripTapMaxMove)
                return true;

            RouteNonAnimatedStripTap(side, idx, changedTouch);
            return true;
        }

        return false;
    }

    /// <summary>Routes a tap on a non-animated strip to its owning session: the plugin-override
    /// provider when active, otherwise the per-segment session (segmented mode). Mirrors the
    /// hit-test math of the legacy immediate-tap path in <c>OnTouchButtonPress</c>.</summary>
    private void RouteNonAnimatedStripTap(RotarySide side, int idx, TouchInfo touch)
    {
        var localX = side == RotarySide.Right ? touch.X - 420 : touch.X;
        var tapX = Math.Clamp(localX, 0, 60);
        var tapY = Math.Clamp(touch.Y, 0, StripHeight);

        if (IsPluginStripActive(side, out var stripSession))
        {
            try { stripSession.OnStripTapped(tapX, tapY); }
            catch (Exception ex) { Console.WriteLine($"Side-strip session tap failed: {ex.Message}"); }
        }
        else if (RouteFreeDrawSegmentTap(side, tapY))
        {
            // Free-draw tap consumed by a per-segment command.
        }
        else if (_segmentSession[idx] is { } segmentSession)
        {
            try { segmentSession.OnStripTapped(tapX, tapY); }
            catch (Exception ex) { Console.WriteLine($"Segment-strip session tap failed: {ex.Message}"); }
        }
    }

    /// <summary>Routes a strip tap to its owning consumer: a free-draw per-segment command
    /// when the page is in <see cref="StripMode.FreeDraw"/>, otherwise the segment session
    /// (segmented mode). Mirrors the legacy tap routing in <c>OnTouchButtonPress</c>.</summary>
    private void RouteStripTap(RotarySide side, int idx, TouchInfo touch)
    {
        var tapY = Math.Clamp(touch.Y, 0, StripHeight);
        if (RouteFreeDrawSegmentTap(side, tapY)) return;

        if (_segmentSession[idx] is not { } segmentSession) return;
        var localX = side == RotarySide.Right ? touch.X - 420 : touch.X;
        var tapX = Math.Clamp(localX, 0, 60);
        try { segmentSession.OnStripTapped(tapX, tapY); }
        catch (Exception ex) { Console.WriteLine($"Segment-strip session tap failed: {ex.Message}"); }
    }

    /// <summary>When the side's current page is in <see cref="StripMode.FreeDraw"/>, maps the
    /// tap's Y to one of three equal vertical segments (top/middle/bottom) and fires that
    /// segment's command. Returns true when a free-draw page consumed the tap (even with no
    /// command bound), so the caller skips the segment/plugin session paths.</summary>
    private bool RouteFreeDrawSegmentTap(RotarySide side, int tapY)
    {
        var page = pageManager.GetCurrentRotaryPage(side);
        if (page is not { StripMode: StripMode.FreeDraw }) return false;

        var segment = Math.Clamp(tapY * RotaryButtonPage.StripSegmentCount / StripHeight,
            0, RotaryButtonPage.StripSegmentCount - 1);

        var command = page.GetStripSegmentCommand(segment);
        if (!string.IsNullOrEmpty(command))
        {
            // Global knob index space (Left 0–2, Right 3–5) so command context / dynamic-text
            // resolution matches a dial press for this segment's position.
            var globalIndex = (side == RotarySide.Right ? RotaryButtonPage.StripSegmentCount : 0) + segment;
            FireAndForget(command, ButtonTargets.RotaryEncoder, globalIndex);
        }

        return true;
    }

    private static double NowMs() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;

    /// <summary>Folds one sample into the drag's smoothed velocity (signed px/ms). The
    /// first sample after the start seeds it; later samples blend (EMA) to ride out the
    /// jitter of individual touch packets.</summary>
    private static void UpdateVelocity(StripDragState st, int y, double nowMs)
    {
        var dt = Math.Max(1.0, nowMs - st.LastMs);
        var inst = (y - st.LastY) / dt;
        st.Velocity = st.LastMs <= st.StartMs ? inst : (st.Velocity * 0.6) + (inst * 0.4);
        st.LastMs = nowMs;
        st.LastY = y;
    }

    /// <summary>Drops any in-flight scheduler-driven transition for a side without committing —
    /// the next transition (or a fresh drag) re-prepares bitmaps from the current page.</summary>
    private void CancelStripTransition(int idx) => _transition[idx].Active = false;

    /// <summary>Cancels any in-flight drags/transitions and frees cached bitmaps for both
    /// strips — called when the device goes off or providers detach.</summary>
    private void ResetStripDrags()
    {
        for (var idx = 0; idx < 2; idx++)
        {
            _drag[idx].Active = false;
            _tapTrack[idx].Active = false;
            CancelStripTransition(idx);
            RetireDragBitmaps(idx);
        }
    }

    // --- Bitmap dispose plumbing (all disposal happens under the per-side gate) -------

    /// <summary>Detaches the side's cached page bitmaps and queues them for gated
    /// disposal. References are cleared before the bitmaps are queued so no in-flight
    /// render can read a bitmap that's about to be freed.</summary>
    private void RetireDragBitmaps(int idx)
    {
        var st = _drag[idx];
        var c = st.Current;
        var n = st.Next;
        var p = st.Prev;
        st.Current = st.Next = st.Prev = null;
        EnqueueDispose(idx, c, n, p);
    }

    private void EnqueueDispose(int idx, params SKBitmap[] bitmaps)
    {
        var q = _stripDisposeQueue[idx];
        var any = false;
        lock (q)
        {
            foreach (var b in bitmaps)
                if (b != null) { q.Add(b); any = true; }
        }
        if (any) _ = DrainGated(idx);
    }

    private async Task DrainGated(int idx)
    {
        await _stripRedrawGate[idx].WaitAsync();
        try { DrainStripDisposeQueue(idx); }
        finally { _stripRedrawGate[idx].Release(); }
    }

    /// <summary>Disposes queued bitmaps. Caller must hold <c>_stripRedrawGate[idx]</c>.</summary>
    private void DrainStripDisposeQueue(int idx)
    {
        var q = _stripDisposeQueue[idx];
        lock (q)
        {
            foreach (var b in q)
            {
                try { b.Dispose(); }
                catch { /* already disposed */ }
            }
            q.Clear();
        }
    }
}
