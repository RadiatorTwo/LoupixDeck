using LoupixDeck.LoupedeckDevice.Device;
using LoupixDeck.Models;
using LoupixDeck.Registry;

namespace LoupixDeck.Services;

public interface IDeviceService
{
    LoupedeckDevice.Device.LoupedeckDevice Device { get; }
    int TouchButtonCount { get; }
    int RotaryButtonCount { get; }
    void StartDevice(string devicePort, int deviceBaudrate);
    void ReconnectDevice();
    Task ShowTemporaryTextButton(int index, string text, int displayDurationMilliseconds);
}

public class LoupedeckDeviceService : IDeviceService
{
    private readonly LoupedeckConfig _config;
    private readonly DeviceRegistry.DeviceInfo _deviceInfo;
    private readonly AutoResetEvent _deviceCreatedEvent = new(false);

    public LoupedeckDevice.Device.LoupedeckDevice Device { get; private set; }

    public int TouchButtonCount => Device?.TouchButtonCount ?? 0;
    public int RotaryButtonCount => Device?.RotaryCount ?? 0;

    public LoupedeckDeviceService(LoupedeckConfig config, DeviceRegistry.DeviceInfo deviceInfo)
    {
        _config = config;
        _deviceInfo = deviceInfo;
    }

    public void StartDevice(string devicePort, int deviceBaudrate)
    {
        var deviceThread = new Thread(() =>
        {
            // The active device type was selected before DI build (App.axaml.cs +
            // ActiveDeviceResolver). Falling back to Live S keeps very
            // old configs that predate the device registry alive.
            var type = _deviceInfo?.DeviceType ?? typeof(LoupedeckLiveSDevice);

            // An unset rate resolves against the registry entry for this model, so the
            // registry stays the source of truth even when the config carries no value.
            if (deviceBaudrate <= 0)
                deviceBaudrate = _deviceInfo?.Baudrate ?? LoupedeckDevice.Constants.DefaultBaudrate;

            Device = (LoupedeckDevice.Device.LoupedeckDevice)Activator.CreateInstance(
                type,
                null, // host
                devicePort,
                deviceBaudrate,
                true, // autoConnect
                LoupedeckDevice.Constants.DefaultReconnectInterval);

            // The user's own key-grid measurement, if their config carries one. Set before
            // anything draws, so the first frame already lands on the calibrated grid.
            Device.KeyCalibration = _config?.EffectiveKeyCalibration;
            _deviceCreatedEvent.Set();
        })
        {
            IsBackground = true
        };
        deviceThread.Start();

        // Bounded wait: the device ctor now always returns promptly (a failed connect
        // is swallowed and retried on a background thread, issue #146), but never block
        // the calling (UI) thread indefinitely if a future ctor path hangs. On timeout
        // throw so the per-device init catch in App.InitializeDevices isolates it.
        if (!_deviceCreatedEvent.WaitOne(TimeSpan.FromSeconds(30)))
            throw new TimeoutException(
                $"Device creation for port '{devicePort}' did not complete within 30 s.");
    }

    /// <summary>
    /// Reconnects the *existing* Device instance so that all event subscribers
    /// (the device controller's OnButton/OnTouch/OnRotate) stay valid.
    /// Replacing the Device reference here would silently break those.
    /// </summary>
    public void ReconnectDevice()
    {
        Device?.Reconnect();
    }

    private int _currentCallId;

    public async Task ShowTemporaryTextButton(int index, string text, int displayDurationMilliseconds)
    {
        var callId = Interlocked.Increment(ref _currentCallId); // Atomically increment the call ID
        const int interval = 100; // Update interval in milliseconds
        var elapsed = 0; // Tracks the elapsed time

        // Rendered once. The loop re-asserts the tile every interval because a key redraw (a
        // dynamic text tick, an animation frame, the page paint) may cover it meanwhile; the
        // device only sends it again when that happened.
        using (SkiaSharp.SKBitmap tile = Device.RenderTextButton(text))
        {
            while (elapsed < displayDurationMilliseconds)
            {
                if (callId != _currentCallId)
                {
                    // Exit if a newer call has been made
                    return;
                }

                await Device.DrawTextButton(index, tile); // Update the text button
                await Task.Delay(interval); // Wait for the specified interval
                elapsed += interval; // Increment the elapsed time
            }
        }

        // Only the last call executes this action
        if (callId == _currentCallId)
        {
            // Put the key back. Its RenderedImage is current: redraws of the key kept rendering
            // while the overlay was up, so only an empty one has to be rendered here.
            await Device.DrawTouchButton(_config.CurrentTouchButtonPage.TouchButtons[index], _config, false);
        }
    }
}
