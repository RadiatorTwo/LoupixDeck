using System.Diagnostics;
using LoupixDeck.Models.Extensions;
using LoupixDeck.Utils;
using SkiaSharp;

namespace LoupixDeck.Controllers;

/// <summary>
/// Horizontal slide transition for the center touch grid on a command/GUI-driven touch page
/// change (there is no center-grid swipe gesture — touch pages only change via commands and the
/// on-screen buttons). Mirrors the rotary <see cref="StripAnimationSource"/>: a single per-device
/// <see cref="Services.Animation.IAnimationSource"/> is driven by the central scheduler (#119) and,
/// while a transition is active, advances a horizontal offset from 0 to ±gridWidth with a cubic
/// ease-out, pushing composite frames to the grid region only (so a Razer's side strips are never
/// clobbered) and landing on the incoming page.
///
/// Wallpaper caveat: <see cref="BitmapHelper.RenderTouchButtonContent"/> resolves the wallpaper from
/// the CURRENT page, so the incoming page is committed in memory first (via
/// <c>ApplyTouchPage(..., draw:false)</c> — state only, no device paint) and only then rendered; the
/// outgoing frame is captured beforehand from the current buttons' cached bitmaps.
/// </summary>
public partial class LoupedeckLiveSController
{
    // Slightly longer than the 150 ms strip settle — the touch grid slides its full width.
    private const int TouchSlideMs = 180;

    private sealed class TouchTransition
    {
        public bool Active;
        public int FromOffset;
        public int Target;          // ±gridWidth (sign from the paging direction)
        public SKBitmap Outgoing;   // composed current-page grid
        public SKBitmap Incoming;   // composed target-page grid
        public string OverlayPageName; // page-name overlay to show on completion, or null
        public long StartTimestamp; // Stopwatch timestamp of the transition's first frame
    }

    private readonly TouchTransition _touchTransition = new();

    // Frames retired by a superseding transition, freed on the scheduler thread (no frame
    // overlaps another for one source, so draining at the top of a frame is race-free).
    private readonly List<SKBitmap> _touchDisposeQueue = [];

    /// <summary>The touch-page transition's registration on the central animation scheduler.
    /// Active only while a slide is in flight. Nested so it can reach the controller internals.</summary>
    private sealed class TouchAnimationSource(LoupedeckLiveSController owner) : Services.Animation.IAnimationSource
    {
        public int TargetFps => 30;
        public bool IsActive => owner._touchTransition.Active;
        public Task RenderFrameAsync(Services.Animation.AnimationRenderContext context)
            => owner.RenderTouchTransitionAsync();
    }

    private TouchAnimationSource _touchAnimationSource;

    /// <summary>Registers the touch-page transition source on the central scheduler (controller init).</summary>
    private void RegisterTouchAnimationSource()
    {
        _touchAnimationSource ??= new TouchAnimationSource(this);
        animationScheduler.Register(_touchAnimationSource);
    }

    /// <summary>Unregisters the touch-page transition source (controller shutdown).</summary>
    private void UnregisterTouchAnimationSource()
    {
        if (_touchAnimationSource != null)
            animationScheduler.Unregister(_touchAnimationSource);
    }

    /// <summary>True when a touch page change should slide: the setting is on, a device is
    /// attached, nothing else owns the display, and there is more than one touch page.</summary>
    private bool TouchAnimationApplicable()
    {
        if (!config.TouchPageTransitionAnimationEnabled) return false;
        if (_isDeviceOff || folderNav.IsActive || exclusiveMode.IsActive || _screensaverActive) return false;
        if (deviceService.Device == null) return false;
        return pageManager.TouchButtonPages.Count > 1;
    }

    /// <summary>Animated "next touch page" (slides the incoming page in from the right).</summary>
    public void AnimateNextTouchPage()
    {
        var count = pageManager.TouchButtonPages.Count;
        if (count <= 0) return;
        var target = (pageManager.CurrentTouchPageIndex + 1) % count;
        AnimateTouchPage(target, direction: -1);
    }

    /// <summary>Animated "previous touch page" (slides the incoming page in from the left).</summary>
    public void AnimatePreviousTouchPage()
    {
        var count = pageManager.TouchButtonPages.Count;
        if (count <= 0) return;
        var target = (pageManager.CurrentTouchPageIndex - 1 + count) % count;
        AnimateTouchPage(target, direction: +1);
    }

    /// <summary>Animated goto-by-index for touch pages, sliding in the shortest wrap direction
    /// (default forward/next on a tie). <paramref name="pageIndex"/> is 0-based.</summary>
    public void AnimateGotoTouchPage(int pageIndex)
    {
        var count = pageManager.TouchButtonPages.Count;
        if (pageIndex < 0 || pageIndex >= count) return;
        var from = pageManager.CurrentTouchPageIndex;
        if (from == pageIndex) return;

        var forward = (((pageIndex - from) % count) + count) % count;
        var direction = forward <= count - forward ? -1 : +1;   // tie → forward (next)
        AnimateTouchPage(pageIndex, direction);
    }

    /// <summary>Orchestrates one touch-page slide: captures the outgoing grid, commits the page
    /// in memory without drawing (so the incoming page becomes current for correct wallpaper),
    /// renders the incoming grid off the UI thread, then arms the scheduler transition. Falls
    /// back to an instant page change when the animation doesn't apply. All page-state mutation
    /// runs on the UI thread.</summary>
    private void AnimateTouchPage(int targetIndex, int direction)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                if (!TouchAnimationApplicable() || _touchTransition.Active
                    || targetIndex == pageManager.CurrentTouchPageIndex)
                {
                    await pageManager.ApplyTouchPage(targetIndex);
                    return;
                }

                var device = deviceService.Device;
                var columns = device.Columns;
                var rows = device.Rows;

                // Outgoing grid from the still-current page's cached slot bitmaps.
                var outgoing = ComposeCurrentTouchGrid(columns, rows);

                // Commit page state only (no device paint) so the incoming page is now current.
                await pageManager.ApplyTouchPage(targetIndex, draw: false);

                var overlayName = !_screensaverActive && config.ShowPageNameOverlayEnabled
                    ? pageManager.CurrentTouchButtonPage?.PageName
                    : null;

                // Render the incoming grid off the UI thread, then hand pacing to the scheduler.
                _ = Task.Run(() =>
                {
                    try
                    {
                        var incoming = RenderTouchGridForCurrentPage(columns, rows);
                        BeginTouchTransition(outgoing, incoming, direction, overlayName);
                    }
                    catch (Exception ex)
                    {
                        outgoing?.Dispose();
                        Console.WriteLine($"Touch page transition render failed: {ex.Message}");
                        // The page was already committed (draw:false) but nothing was painted —
                        // paint the settled page authoritatively so the device isn't left showing
                        // the old page.
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = RedrawCurrentTouchPage());
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Touch page transition failed: {ex.Message}");
                try { await pageManager.ApplyTouchPage(targetIndex); } catch { /* best effort */ }
            }
        });
    }

    /// <summary>Composes the current page's grid region from the buttons' cached rendered images.</summary>
    private SKBitmap ComposeCurrentTouchGrid(int columns, int rows)
    {
        var page = config.CurrentTouchButtonPage;
        var slots = new SKBitmap[columns * rows];
        if (page?.TouchButtons != null)
            for (var i = 0; i < slots.Length; i++)
                slots[i] = page.TouchButtons.FindByIndex(i)?.RenderedImage;
        return BitmapHelper.ComposeTouchGrid(slots, columns, rows);
    }

    /// <summary>Renders every grid button of the (now current) incoming page and composes them
    /// into a grid-region bitmap. Also refreshes each button's RenderedImage for the UI mirror.</summary>
    private SKBitmap RenderTouchGridForCurrentPage(int columns, int rows)
    {
        var page = config.CurrentTouchButtonPage;
        var xOffset = deviceService.Device?.WallpaperGridXOffset ?? 0;
        var slots = new SKBitmap[columns * rows];
        if (page?.TouchButtons != null)
            for (var i = 0; i < slots.Length; i++)
            {
                var button = page.TouchButtons.FindByIndex(i);
                if (button != null)
                    slots[i] = BitmapHelper.RenderTouchButtonContent(button, config, 90, 90, columns, xOffset);
            }
        return BitmapHelper.ComposeTouchGrid(slots, columns, rows);
    }

    /// <summary>Arms a touch-page transition and wakes the scheduler. Supersedes any in-flight
    /// one, retiring its frames through the gated dispose queue.</summary>
    private void BeginTouchTransition(SKBitmap outgoing, SKBitmap incoming, int direction, string overlayName)
    {
        var tr = _touchTransition;
        lock (_touchDisposeQueue)
        {
            if (tr.Outgoing != null) _touchDisposeQueue.Add(tr.Outgoing);
            if (tr.Incoming != null) _touchDisposeQueue.Add(tr.Incoming);
        }

        tr.Outgoing = outgoing;
        tr.Incoming = incoming;
        tr.FromOffset = 0;
        tr.Target = direction < 0 ? -outgoing.Width : outgoing.Width;
        tr.OverlayPageName = overlayName;
        tr.StartTimestamp = Stopwatch.GetTimestamp();
        tr.Active = true;

        _touchAnimationSource ??= new TouchAnimationSource(this);
        animationScheduler.RequestFrame(_touchAnimationSource);
    }

    /// <summary>One scheduler frame for the touch-page slide: advances the horizontal offset by
    /// wall-clock progress (cubic ease-out), pushes the composite to the grid region, and on the
    /// final frame lands on the incoming page and shows the page-name overlay if enabled. Runs on
    /// a scheduler background thread; the scheduler never overlaps frames for this source.</summary>
    private async Task RenderTouchTransitionAsync()
    {
        // Free frames retired by a superseding transition (safe: no frame overlaps this one).
        lock (_touchDisposeQueue)
        {
            foreach (var bmp in _touchDisposeQueue)
                bmp?.Dispose();
            _touchDisposeQueue.Clear();
        }

        var tr = _touchTransition;
        if (!tr.Active) return;

        var outgoing = tr.Outgoing;
        var incoming = tr.Incoming;
        if (outgoing == null || incoming == null)
        {
            tr.Active = false;
            return;
        }

        // Another feature took over the display mid-slide (device off, screensaver, folder,
        // exclusive) → abort. The page was already committed, so the takeover's own redraw
        // (e.g. OnScreensaverStopped) paints the settled page.
        if (_isDeviceOff || folderNav.IsActive || exclusiveMode.IsActive || _screensaverActive)
        {
            tr.Active = false;
            tr.Outgoing = null;
            tr.Incoming = null;
            tr.OverlayPageName = null;
            outgoing.Dispose();
            incoming.Dispose();
            return;
        }

        try
        {
            var elapsedMs = Stopwatch.GetElapsedTime(tr.StartTimestamp).TotalMilliseconds;
            var t = Math.Clamp(elapsedMs / TouchSlideMs, 0.0, 1.0);
            var eased = 1 - Math.Pow(1 - t, 3); // cubic ease-out
            var offset = (int)Math.Round(tr.FromOffset + ((tr.Target - tr.FromOffset) * eased));

            if (t >= 1.0)
            {
                // Final frame: the incoming grid exactly, at rest.
                await PushTouchGrid(incoming);
                tr.Active = false;
                tr.Outgoing = null;
                tr.Incoming = null;
                outgoing.Dispose();
                incoming.Dispose();

                var name = tr.OverlayPageName;
                tr.OverlayPageName = null;
                if (!string.IsNullOrEmpty(name))
                    _ = deviceService.ShowTemporaryTextButton(0, name, 2000);
                return;
            }

            using var frame = BitmapHelper.ComposeHorizontalSlide(outgoing, incoming, offset);
            await PushTouchGrid(frame);
        }
        catch (Exception ex)
        {
            tr.Active = false;
            Console.WriteLine($"Touch page transition frame failed: {ex.Message}");
        }
    }

    /// <summary>Pushes a grid-region bitmap to the center display (device picks the x-origin).</summary>
    private Task PushTouchGrid(SKBitmap grid)
    {
        var device = deviceService.Device;
        return device == null ? Task.CompletedTask : device.DrawCenterGridRegion(grid, refresh: true);
    }
}
