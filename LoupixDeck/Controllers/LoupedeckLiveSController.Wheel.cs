using System.Diagnostics;
using LoupixDeck.LoupedeckDevice;
using LoupixDeck.PluginSdk;
using LoupixDeck.Utils;
using Device = LoupixDeck.LoupedeckDevice.Device.LoupedeckDevice;

namespace LoupixDeck.Controllers;

/// <summary>
/// The Loupedeck CT's centre wheel: the large dial with its own round 240×240 touchscreen.
/// It behaves like a seventh rotary (global index <see cref="Device.WheelRotaryIndex"/>) whose
/// binding lives on the current touch page (<see cref="Models.TouchButtonPage.Wheel"/>), so it
/// follows page, workspace and profile switches without a paging model of its own.
/// <para>
/// Turning it arrives as ordinary KNOB_ROTATE frames and goes through <see cref="OnRotate"/>.
/// It has no press button code: pushing the wheel shows up as touches on its screen, so a short,
/// still touch there is treated as the press, and a horizontal swipe across it switches workspace
/// (left = next, matching the slide direction of the touch-page transition).
/// </para>
/// <para>
/// A page can hold several wheel modes (<see cref="Models.TouchButtonPage.WheelModes"/>). A vertical
/// swipe opens the mode menu on the wheel screen and moves its highlight; turning the wheel scrolls
/// it too, a tap switches to the highlighted mode, and it closes by itself after a few idle seconds
/// without changing anything.
/// </para>
/// </summary>
public partial class LoupedeckLiveSController
{
    // A tap must stay within this many pixels of where it started (on the 240px screen) and
    // lift within the time limit, so a finger resting on or dragged across the screen is not a
    // press.
    private const int WheelTapMaxTravel = 40;
    private const int WheelTapMaxMs = 800;

    // One push of the wheel can report a short burst of start/end pairs; taps closer together
    // than this are one press.
    private const int WheelTapDebounceMs = 250;

    // A swipe must travel at least this far horizontally (on the 240px screen) and clearly more
    // than it moved vertically, so a press that wobbles or a vertical drag never switches.
    private const int WheelSwipeMinTravel = 60;
    private const double WheelSwipeDominance = 1.5;

    // Touch-down position and time per firmware touch id, and the latest position seen while the
    // finger moves. Only touched from the serial read thread, which raises every wheel touch.
    private readonly Dictionary<byte, (int X, int Y, long Timestamp)> _wheelTouchStarts = new();
    private readonly Dictionary<byte, (int X, int Y)> _wheelTouchLast = new();
    private long _lastWheelTapTimestamp;

    // Mode menu state. Touched from the serial read thread (touches, turns) and the auto-close
    // timer, hence the lock. The menu belongs to the page it was opened on: a page change closes it.
    private readonly Lock _wheelMenuLock = new();
    private Models.TouchButtonPage _wheelMenuPage;
    private int _wheelMenuHighlight;
    private CancellationTokenSource _wheelMenuCloseCts;
    private const int WheelMenuIdleCloseMs = 4000;

    // Serialises wheel redraws and coalesces bursts — an adjustment command turned quickly
    // asks for one repaint per detent. Same scheme as the exclusive-mode redraw gate.
    private readonly SemaphoreSlim _wheelRedrawGate = new(1, 1);
    private long _wheelRequestedGen;
    private long _wheelDrawnGen;

    private bool IsWheelIndex(int globalIndex) =>
        globalIndex == Device.WheelRotaryIndex && deviceService.Device?.HasWheel == true;

    /// <summary>
    /// Repaints the wheel screen from the current touch page's wheel binding — public entry for
    /// the UI after the user edits the wheel. A no-op on devices without a wheel.
    /// </summary>
    public Task RefreshWheel() => RedrawWheel();

    /// <summary>
    /// Renders the current page's wheel binding and pushes it to the wheel screen, mirroring the
    /// frame onto <see cref="Models.RotaryButton.RenderedImage"/> for the device view. Skipped
    /// while something else owns the hardware; its release repaints through
    /// <see cref="RedrawSideStrips"/>.
    /// </summary>
    private async Task RedrawWheel()
    {
        var device = deviceService.Device;
        if (device is not { HasWheel: true }) return;
        if (_isDeviceOff || _screensaverActive || _fullDisplayActive) return;

        var requested = Interlocked.Increment(ref _wheelRequestedGen);

        await _wheelRedrawGate.WaitAsync();
        try
        {
            // Coalesced away: an earlier waiter already drew state at least this fresh.
            if (Interlocked.Read(ref _wheelDrawnGen) >= requested) return;
            var snapshot = Interlocked.Read(ref _wheelRequestedGen);

            var page = config.CurrentTouchButtonPage;
            var wheel = page?.Wheel;
            if (wheel == null) return;

            SkiaSharp.SKBitmap frame;
            if (TryGetOpenWheelMenu(page, out var highlight))
            {
                var labels = page.WheelModes.Select((mode, i) =>
                    string.IsNullOrWhiteSpace(mode?.DisplayText) ? $"Mode {i + 1}" : mode.DisplayText).ToList();
                frame = BitmapHelper.RenderWheelMenu(labels, highlight, device.WheelScreenSize);
            }
            else
            {
                // Resolved before rendering, outside the Skia gate — see RenderStripFor.
                AdjustmentValue? value = ResolveDialAdjustmentValue(wheel, Device.WheelRotaryIndex);
                frame = BitmapHelper.RenderWheelScreen(wheel, value, device.WheelScreenSize);
            }

            // The setter owns the bitmap's lifetime (deferred dispose); the push below and the
            // UI binding both read it, so it is not disposed here.
            wheel.RenderedImage = frame;
            await device.DrawWheelScreen(frame);

            Interlocked.Exchange(ref _wheelDrawnGen, snapshot);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Wheel redraw failed: {ex.Message}");
        }
        finally
        {
            _wheelRedrawGate.Release();
        }
    }

    /// <summary>
    /// Turns touches on the wheel screen into presses and workspace swipes. The firmware reports a
    /// held finger as repeated TOUCH_START frames for the same id, so only the first one records the
    /// start; the later ones track where the finger has moved to.
    /// </summary>
    private void OnWheelTouch(object sender, TouchEventArgs e)
    {
        var touch = e.ChangedTouch;
        if (touch == null) return;

        if (e.EventType != Constants.TouchEventType.TOUCH_END)
        {
            if (_wheelTouchStarts.ContainsKey(touch.Id))
            {
                _wheelTouchLast[touch.Id] = (touch.X, touch.Y);
                return;
            }

            // Any input resets the screensaver idle timer; a touch that stops a running
            // screensaver was a wake gesture, so its release must not press.
            using (router.Enter(serviceProvider))
            {
                if (screensaver.NotifyActivity()) return;
            }

            _wheelTouchStarts[touch.Id] = (touch.X, touch.Y, Stopwatch.GetTimestamp());
            _wheelTouchLast[touch.Id] = (touch.X, touch.Y);
            return;
        }

        if (!_wheelTouchStarts.Remove(touch.Id, out var start)) return;
        _wheelTouchLast.Remove(touch.Id, out var last);

        // The end frame normally carries the lift-off position; fall back to the last move if it
        // reports nothing past the start.
        var endX = touch.X;
        var endY = touch.Y;
        if (endX == start.X && endY == start.Y)
            (endX, endY) = last;

        var dx = endX - start.X;
        var dy = endY - start.Y;
        if (Math.Abs(dx) >= WheelSwipeMinTravel && Math.Abs(dx) >= Math.Abs(dy) * WheelSwipeDominance)
        {
            CloseWheelMenu(redraw: false);
            OnWheelSwiped(next: dx < 0);
            return;
        }

        if (Math.Abs(dy) >= WheelSwipeMinTravel && Math.Abs(dy) >= Math.Abs(dx) * WheelSwipeDominance)
        {
            // Swiping up scrolls the list up, bringing the next mode into the middle.
            StepWheelMenu(dy < 0 ? 1 : -1);
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var travel = Math.Max(Math.Abs(dx), Math.Abs(dy));
        if (travel > WheelTapMaxTravel || StopwatchMs(now - start.Timestamp) > WheelTapMaxMs)
            return;

        if (_lastWheelTapTimestamp != 0 && StopwatchMs(now - _lastWheelTapTimestamp) < WheelTapDebounceMs)
            return;
        _lastWheelTapTimestamp = now;

        if (TrySelectWheelMenuHighlight()) return;
        OnWheelPressed();
    }

    /// <summary>
    /// True (with the highlighted index) while the mode menu is open on <paramref name="page"/>.
    /// A menu left open on another page is stale and closed here.
    /// </summary>
    private bool TryGetOpenWheelMenu(Models.TouchButtonPage page, out int highlight)
    {
        lock (_wheelMenuLock)
        {
            highlight = _wheelMenuHighlight;
            if (_wheelMenuPage == null) return false;
            if (ReferenceEquals(_wheelMenuPage, page) && page.WheelModes.Count > 1) return true;

            _wheelMenuPage = null;
            _wheelMenuCloseCts?.Cancel();
            return false;
        }
    }

    /// <summary>
    /// Opens the mode menu (on the active mode) or moves its highlight by <paramref name="step"/>,
    /// wrapping, and restarts the idle close. A no-op on a page with a single mode, or while
    /// something else owns the wheel.
    /// </summary>
    private void StepWheelMenu(int step)
    {
        using var _routerScope = router.Enter(serviceProvider);
        if (StopFullDisplayOnInput()) return;
        if (_isDeviceOff || folderNav.IsActive || exclusiveMode.Owns(ExclusiveControlScope.RotaryPress))
            return;

        var page = config.CurrentTouchButtonPage;
        var count = page?.WheelModes.Count ?? 0;
        if (count < 2) return;

        lock (_wheelMenuLock)
        {
            if (!ReferenceEquals(_wheelMenuPage, page))
            {
                _wheelMenuPage = page;
                _wheelMenuHighlight = Math.Clamp(page.WheelModeIndex, 0, count - 1);
            }

            _wheelMenuHighlight = ((_wheelMenuHighlight + step) % count + count) % count;
            RestartWheelMenuCloseTimer();
        }

        _ = RedrawWheel();
    }

    /// <summary>While the mode menu is open, a wheel turn scrolls its highlight instead of running the
    /// active mode's command. Returns true when the turn was consumed.</summary>
    private bool TryScrollWheelMenu(int delta)
    {
        if (!TryGetOpenWheelMenu(config.CurrentTouchButtonPage, out _)) return false;
        StepWheelMenu(Math.Sign(delta));
        return true;
    }

    /// <summary>Tap while the mode menu is open: activate the highlighted mode and close the menu.</summary>
    private bool TrySelectWheelMenuHighlight()
    {
        var page = config.CurrentTouchButtonPage;
        if (!TryGetOpenWheelMenu(page, out var highlight)) return false;

        CloseWheelMenu(redraw: false);
        SelectWheelModeIndex(page, highlight);
        return true;
    }

    private void RestartWheelMenuCloseTimer()
    {
        _wheelMenuCloseCts?.Cancel();
        var cts = _wheelMenuCloseCts = new CancellationTokenSource();
        _ = Task.Delay(WheelMenuIdleCloseMs, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            lock (_wheelMenuLock)
            {
                if (!ReferenceEquals(_wheelMenuCloseCts, cts)) return;
            }
            CloseWheelMenu(redraw: true);
        }, TaskScheduler.Default);
    }

    private void CloseWheelMenu(bool redraw)
    {
        lock (_wheelMenuLock)
        {
            if (_wheelMenuPage == null) return;
            _wheelMenuPage = null;
            _wheelMenuCloseCts?.Cancel();
        }

        if (redraw) _ = RedrawWheel();
    }

    private void SelectWheelModeIndex(Models.TouchButtonPage page, int index)
    {
        if (page == null || page.WheelModes.Count == 0) return;
        page.WheelModeIndex = ((index % page.WheelModes.Count) + page.WheelModes.Count) % page.WheelModes.Count;
        SaveConfig();
        _ = RedrawWheel();
    }

    // ───────── Wheel-mode editing (device view pager) ─────────

    /// <summary>Activates the next/previous wheel mode of the current page (wrapping).</summary>
    public void StepWheelMode(int step)
    {
        var page = config.CurrentTouchButtonPage;
        if (page == null) return;
        CloseWheelMenu(redraw: false);
        SelectWheelModeIndex(page, page.WheelModeIndex + step);
    }

    /// <summary>Adds an empty wheel mode after the active one and activates it.</summary>
    public void AddWheelMode()
    {
        var page = config.CurrentTouchButtonPage;
        if (page == null) return;
        CloseWheelMenu(redraw: false);

        var insertAt = Math.Clamp(page.WheelModeIndex + 1, 0, page.WheelModes.Count);
        page.WheelModes.Insert(insertAt, Models.TouchButtonPage.NewWheelMode());
        page.NotifyWheelModesChanged();
        SelectWheelModeIndex(page, insertAt);
    }

    /// <summary>Deletes the active wheel mode; the last remaining mode is never deleted.</summary>
    public void DeleteWheelMode()
    {
        var page = config.CurrentTouchButtonPage;
        if (page == null || page.WheelModes.Count < 2) return;
        CloseWheelMenu(redraw: false);

        var index = Math.Clamp(page.WheelModeIndex, 0, page.WheelModes.Count - 1);
        page.WheelModes.RemoveAt(index);
        page.NotifyWheelModesChanged();
        SelectWheelModeIndex(page, Math.Min(index, page.WheelModes.Count - 1));
    }

    /// <summary>
    /// Switches to the next or previous workspace of the active profile (wrapping), through the
    /// same commands a button would bind, so the switch pins auto-switching and repaints exactly as
    /// any manual workspace change. Ignored while the device is off, a folder is open, or an
    /// exclusive provider owns the wheel's press; a display takeover consumes it as a wake.
    /// </summary>
    private void OnWheelSwiped(bool next)
    {
        using var _routerScope = router.Enter(serviceProvider);

        if (StopFullDisplayOnInput()) return;
        if (_isDeviceOff || folderNav.IsActive || exclusiveMode.Owns(ExclusiveControlScope.RotaryPress))
            return;

        FireAndForget(next ? "System.NextWorkspace" : "System.PreviousWorkspace", ButtonTargets.RotaryEncoder,
            Device.WheelRotaryIndex);
    }

    /// <summary>
    /// Runs the wheel's press, with the same precedence as a side dial's press in
    /// <see cref="OnSimpleButtonPress"/>: a display takeover, an exclusive provider that owns
    /// rotary presses, an open folder's rotary override, then the user's binding.
    /// </summary>
    private void OnWheelPressed()
    {
        using var _routerScope = router.Enter(serviceProvider);

        if (StopFullDisplayOnInput()) return;

        const int index = Device.WheelRotaryIndex;

        if (exclusiveMode.Owns(ExclusiveControlScope.RotaryPress))
        {
            try { exclusiveMode.Current?.OnRotaryPressed(index); }
            catch (Exception ex) { Console.WriteLine($"Exclusive wheel press: {ex.Message}"); }
            return;
        }

        if (folderNav.IsActive)
        {
            if (folderNav.CurrentProvider?.RotaryOverrides is { } overrides &&
                overrides.TryGetValue(index, out var ov) && ov.OnPress != null)
            {
                try { ov.OnPress().GetAwaiter().GetResult(); }
                catch (Exception ex) { Console.WriteLine($"Folder wheel press failed: {ex.Message}"); }
            }
            return;
        }

        var wheel = config.CurrentTouchButtonPage?.Wheel;
        if (wheel == null) return;
        if (_isDeviceOff && !wheel.EnableWhenOff) return;

        var command = RotaryPressCommand(wheel);
        if (command == null) return;

        FireAndForget(command, ButtonTargets.RotaryEncoder, index);
    }
}
