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
/// still touch there is treated as the press.
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

    // Touch-down position and time per firmware touch id. Only touched from the serial read
    // thread, which raises every wheel touch.
    private readonly Dictionary<byte, (int X, int Y, long Timestamp)> _wheelTouchStarts = new();
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
    /// Turns touches on the wheel screen into presses. The firmware reports a held finger as
    /// repeated TOUCH_START frames for the same id, so only the first one records the start.
    /// </summary>
    private void OnWheelTouch(object sender, TouchEventArgs e)
    {
        var touch = e.ChangedTouch;
        if (touch == null) return;

        if (e.EventType != Constants.TouchEventType.TOUCH_END)
        {
            if (_wheelTouchStarts.ContainsKey(touch.Id)) return;

            // Any input resets the screensaver idle timer; a touch that stops a running
            // screensaver was a wake gesture, so its release must not press.
            using (router.Enter(serviceProvider))
            {
                if (screensaver.NotifyActivity()) return;
            }

            _wheelTouchStarts[touch.Id] = (touch.X, touch.Y, Stopwatch.GetTimestamp());
            return;
        }

        if (!_wheelTouchStarts.Remove(touch.Id, out var start)) return;

        var now = Stopwatch.GetTimestamp();
        var travel = Math.Max(Math.Abs(touch.X - start.X), Math.Abs(touch.Y - start.Y));
        if (travel > WheelTapMaxTravel || StopwatchMs(now - start.Timestamp) > WheelTapMaxMs)
            return;

        if (_lastWheelTapTimestamp != 0 && StopwatchMs(now - _lastWheelTapTimestamp) < WheelTapDebounceMs)
            return;
        _lastWheelTapTimestamp = now;

        OnWheelPressed();
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
