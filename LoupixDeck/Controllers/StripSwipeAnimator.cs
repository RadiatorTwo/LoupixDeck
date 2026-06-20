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
        public CancellationTokenSource SettleCts;
    }

    private readonly StripDragState[] _drag = [new(), new()];

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
    private bool IsStripDragBusy(int idx) => _drag[idx].Active || _drag[idx].SettleCts != null;

    /// <summary>True when the side's current page supports the finger-follow animation:
    /// a side-strip device, not off/exclusive/folder, a non-plugin page, and more than
    /// one page to slide between.</summary>
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
        CancelSettle(idx);

        // Render the fresh page bitmaps first, then retire the old ones — so any gated
        // render in flight reads the new references, never a queued-for-dispose bitmap.
        var oldC = st.Current;
        var oldN = st.Next;
        var oldP = st.Prev;

        var current = pageManager.GetCurrentRotaryPage(side);
        var c = RenderStripFor(current, side, useSessions: true);
        var next = pageManager.PeekRotaryPage(side, +1);
        var prev = pageManager.PeekRotaryPage(side, -1);
        var n = next != null ? RenderStripFor(next, side, useSessions: false) : null;
        var p = prev != null ? RenderStripFor(prev, side, useSessions: false) : null;

        var nowMs = NowMs();
        st.Current = c;
        st.Next = n;
        st.Prev = p;
        st.StartY = y;
        st.Offset = 0;
        st.Moved = false;
        st.StartMs = nowMs;
        st.LastMs = nowMs;
        st.LastY = y;
        st.Velocity = 0;
        st.TouchId = touchId;
        st.Active = true;

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
        if (slotButton != null)
            slotButton.RenderedImage = strip;

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
            _ = SettleStripDrag(side, idx, st.Offset, commit, direction);
            return true;
        }

        return false;
    }

    /// <summary>Animates the strip from its current offset to the target (0 for
    /// snap-back, ±height for a commit), then applies the page change on commit.</summary>
    private async Task SettleStripDrag(RotarySide side, int idx, int fromOffset, bool commit, int direction)
    {
        var target = !commit ? 0 : (direction < 0 ? -StripHeight : StripHeight);
        var cts = new CancellationTokenSource();
        _drag[idx].SettleCts = cts;
        var token = cts.Token;

        try
        {
            var steps = Math.Max(1, StripSettleMs / StripMinRedrawMs);
            for (var s = 1; s <= steps; s++)
            {
                if (token.IsCancellationRequested) return;
                var t = s / (double)steps;
                var eased = 1 - Math.Pow(1 - t, 3); // cubic ease-out
                _drag[idx].Offset = (int)Math.Round(fromOffset + (target - fromOffset) * eased);
                await PushAnimationFrame(side, idx);
            }

            if (token.IsCancellationRequested) return;
            _drag[idx].Offset = target;

            if (commit)
            {
                // The neighbour is now fully shown; committing makes it the current page
                // and OnRotaryPageChanged → DrawSideStrip redraws identical pixels at 0.
                if (direction < 0) pageManager.NextRotaryPage(side);
                else pageManager.PreviousRotaryPage(side);
            }
            else
            {
                // Restore the authoritative live strip (resumes segment updates) at 0.
                await RedrawStripCoalesced(side, idx);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Side-strip settle failed ({side}): {ex.Message}");
        }
        finally
        {
            // Only clear/dispose if we still own the settle; if a new drag superseded us,
            // CancelSettle already disposed this cts and owns the bitmaps. Cached bitmaps
            // are retired by the next BeginStripDrag, so the settle never frees them.
            if (ReferenceEquals(_drag[idx].SettleCts, cts))
            {
                _drag[idx].SettleCts = null;
                cts.Dispose();
            }
        }
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
        st.Velocity = st.LastMs <= st.StartMs ? inst : st.Velocity * 0.6 + inst * 0.4;
        st.LastMs = nowMs;
        st.LastY = y;
    }

    private void CancelSettle(int idx)
    {
        var cts = _drag[idx].SettleCts;
        _drag[idx].SettleCts = null;
        if (cts == null) return;
        try { cts.Cancel(); cts.Dispose(); }
        catch { /* already disposed */ }
    }

    /// <summary>Cancels any in-flight drags/settles and frees cached bitmaps for both
    /// strips — called when the device goes off or providers detach.</summary>
    private void ResetStripDrags()
    {
        for (var idx = 0; idx < 2; idx++)
        {
            _drag[idx].Active = false;
            _tapTrack[idx].Active = false;
            CancelSettle(idx);
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
