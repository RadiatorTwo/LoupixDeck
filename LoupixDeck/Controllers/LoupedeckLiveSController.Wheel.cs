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

            var wheel = config.CurrentTouchButtonPage?.Wheel;
            if (wheel == null) return;

            // Resolved before rendering, outside the Skia gate — see RenderStripFor.
            AdjustmentValue? value = ResolveDialAdjustmentValue(wheel, Device.WheelRotaryIndex);
            var frame = BitmapHelper.RenderWheelScreen(wheel, value, device.WheelScreenSize);

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
            OnWheelSwiped(next: dx < 0);
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var travel = Math.Max(Math.Abs(dx), Math.Abs(dy));
        if (travel > WheelTapMaxTravel || StopwatchMs(now - start.Timestamp) > WheelTapMaxMs)
            return;

        if (_lastWheelTapTimestamp != 0 && StopwatchMs(now - _lastWheelTapTimestamp) < WheelTapDebounceMs)
            return;
        _lastWheelTapTimestamp = now;

        OnWheelPressed();
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
