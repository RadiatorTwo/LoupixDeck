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

    /// <summary>True while <see cref="ShowTemporaryTextButton"/> shows its tile on key
    /// <paramref name="index"/>. Redraws of that key stand back meanwhile, so an animated or
    /// live key does not flicker against the tile; the key is repainted when the tile goes.</summary>
    bool IsShowingTemporaryText(int index);

    /// <summary>The tile <see cref="ShowTemporaryTextButton"/> shows on key <paramref name="index"/>,
    /// or null. Only valid under <see cref="Utils.SkiaRenderGate.Sync"/>: the tile is released
    /// under the gate once it goes.</summary>
    SkiaSharp.SKBitmap GetTemporaryTextTile(int index);

    /// <summary>
    /// Set by a playing video wallpaper, which owns the whole panel: a single key written on its
    /// own would be covered by the next frame and tear against it. Called with the key index and
    /// whether the tile is being shown (true) or taken down (false); returns true when the clip
    /// took the key over — it then draws the tile, or the restored key, into its own frames.
    /// </summary>
    Func<int, bool, bool> ClipKeyRedraw { get; set; }
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

    // Key the temporary text tile is on, or -1. Read by the key redraw paths on other threads.
    private volatile int _temporaryTextIndex = -1;

    // The tile itself, swapped and released under SkiaRenderGate.Sync: the video wallpaper's
    // overlay draws it from its render thread.
    private SkiaSharp.SKBitmap _temporaryTextTile;

    public bool IsShowingTemporaryText(int index) => index >= 0 && _temporaryTextIndex == index;

    public SkiaSharp.SKBitmap GetTemporaryTextTile(int index) =>
        IsShowingTemporaryText(index) ? _temporaryTextTile : null;

    public Func<int, bool, bool> ClipKeyRedraw { get; set; }

    public async Task ShowTemporaryTextButton(int index, string text, int displayDurationMilliseconds)
    {
        var callId = Interlocked.Increment(ref _currentCallId); // Atomically increment the call ID
        const int interval = 100; // Update interval in milliseconds
        var elapsed = 0; // Tracks the elapsed time

        // Rendered once. The key's own redraws stand back while the tile is up (see
        // IsShowingTemporaryText); the loop still re-asserts it every interval against what does
        // not, such as a page paint, and the device only sends it again when it was covered.
        SkiaSharp.SKBitmap tile = Device.RenderTextButton(text);
        lock (Utils.SkiaRenderGate.Sync)
        {
            _temporaryTextTile = tile;
            _temporaryTextIndex = index;
        }

        try
        {
            while (elapsed < displayDurationMilliseconds)
            {
                if (callId != _currentCallId)
                {
                    // Exit if a newer call has been made
                    return;
                }

                // A playing clip draws the tile into its frames instead.
                if (ClipKeyRedraw?.Invoke(index, true) != true)
                    await Device.DrawTextButton(index, tile); // Update the text button
                await Task.Delay(interval); // Wait for the specified interval
                elapsed += interval; // Increment the elapsed time
            }
        }
        finally
        {
            // A newer call has already put its own tile in place; only this call's is released.
            lock (Utils.SkiaRenderGate.Sync)
            {
                if (ReferenceEquals(_temporaryTextTile, tile))
                {
                    _temporaryTextTile = null;
                    _temporaryTextIndex = -1;
                }

                tile.Dispose();
            }
        }

        // Only the last call executes this action
        if (callId == _currentCallId)
        {
            // Put the key back, rendered fresh: its own redraws were held back while the tile
            // was up, so an animation or a live value has moved on since its last render.
            if (ClipKeyRedraw?.Invoke(index, false) != true)
                await Device.DrawTouchButton(_config.CurrentTouchButtonPage.TouchButtons[index], _config, true);
        }
    }
}
