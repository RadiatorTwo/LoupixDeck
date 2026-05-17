using System.ComponentModel;
using LoupixDeck.LoupedeckDevice;
using LoupixDeck.Models;
using LoupixDeck.Models.Extensions;
using LoupixDeck.Models.Layers;
using LoupixDeck.Registry;
using LoupixDeck.Services;
using LoupixDeck.Services.FolderNavigation;
using LoupixDeck.Utils;

namespace LoupixDeck.Controllers;

/// <summary>
/// Device-agnostic controller orchestrating the services:
/// - loads/saves the per-device configuration,
/// - starts the device (concrete type chosen via <see cref="DeviceRegistry"/>),
/// - registers device events,
/// - forwards UI events to the corresponding services.
///
/// The class name is kept for source-history continuity (originally Live-S-only);
/// it now handles any device exposed via <see cref="IDeviceService"/>.
/// </summary>
public class LoupedeckLiveSController(
    IDeviceService deviceService,
    ICommandService commandService,
    IPageManager pageManager,
    IConfigService configService,
    IFolderNavigationService folderNav,
    IAssetService assetService,
    INativeHapticService nativeHapticService,
    LoupedeckConfig config,
    DeviceRegistry.DeviceInfo deviceInfo) : IDeviceController
{
    private readonly string _configPath = deviceInfo != null
        ? FileDialogHelper.GetConfigPath(deviceInfo)
        : FileDialogHelper.GetConfigPath("config.json");

    public IPageManager PageManager => pageManager;

    public LoupedeckConfig Config => config;

    private volatile bool _isDeviceOff;
    public bool IsDeviceOff => _isDeviceOff;

    // Tracks the slot index of the currently active touch contact. Set on the
    // first TOUCH_START of a finger-down sequence, cleared on TOUCH_END.
    private int? _activeTouchSlot;

    public async Task ClearDeviceState()
    {
        if (_isDeviceOff) return;
        _isDeviceOff = true;
        try
        {
            var device = deviceService.Device;
            if (device == null) return;
            await device.SetBrightness(0);
            if (config.SimpleButtons != null)
            {
                foreach (var btn in config.SimpleButtons)
                {
                    if (btn == null) continue;
                    await device.SetButtonColor(btn.Id, Avalonia.Media.Colors.Black);
                }
            }
            // Silence firmware-level haptic so touches on the dark screen don't buzz.
            try { device.DisableNativeHaptic(); } catch { /* device may be gone */ }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ClearDeviceState failed: {ex.Message}");
        }
    }

    public async Task RestoreDeviceState()
    {
        if (!_isDeviceOff) return;
        _isDeviceOff = false;
        try
        {
            var device = deviceService.Device;
            if (device == null) return;
            await device.SetBrightness(config.Brightness / 100.0);
            if (config.SimpleButtons != null)
            {
                foreach (var btn in config.SimpleButtons)
                {
                    if (btn == null) continue;
                    await device.SetButtonColor(btn.Id, btn.ButtonColor);
                }
            }
            if (config.CurrentTouchButtonPage?.TouchButtons != null)
            {
                foreach (var tb in config.CurrentTouchButtonPage.TouchButtons)
                {
                    await device.DrawTouchButton(tb, config, true, device.Columns);
                }
            }
            // Re-program firmware haptic from the persisted config.
            nativeHapticService.Apply();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"RestoreDeviceState failed: {ex.Message}");
        }
    }

    public Task ToggleDeviceState() => _isDeviceOff ? RestoreDeviceState() : ClearDeviceState();

    public async Task Initialize(string port = null, int baudrate = 0)
    {
        if (port != null)
            Config.DevicePort = port;

        if (baudrate > 0)
            Config.DeviceBaudrate = baudrate;

        // Stamp the active device's VID/PID into the config so subsequent
        // launches load the right per-device file via ActiveDeviceResolver.
        if (deviceInfo != null)
        {
            Config.DeviceVid = deviceInfo.VendorId;
            Config.DevicePid = deviceInfo.ProductId;
        }

        // Re-detect the current port via VID/PID. The OS may have assigned a
        // different COM/ttyACM number since the last save (USB reconnect, suspend
        // wake-up, hub change). Skip when the user just picked a port explicitly
        // via InitSetup — that's an authoritative override.
        if (port == null && !string.IsNullOrEmpty(Config.DeviceVid) && !string.IsNullOrEmpty(Config.DevicePid))
        {
            try
            {
                var current = SerialDeviceHelper.ListSerialUsbDevices()
                    .FirstOrDefault(d =>
                        string.Equals(d.Vid, Config.DeviceVid, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(d.Pid, Config.DevicePid, StringComparison.OrdinalIgnoreCase));

                if (current != null && !string.IsNullOrEmpty(current.DevNode) &&
                    !string.Equals(current.DevNode, Config.DevicePort, StringComparison.Ordinal))
                {
                    Console.WriteLine($"[Port] {Config.DeviceVid}:{Config.DevicePid} moved {Config.DevicePort} → {current.DevNode}");
                    Config.DevicePort = current.DevNode;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Port] re-detection failed: {ex.Message}");
            }
        }

        // Start the device using the configuration
        deviceService.StartDevice(config.DevicePort, config.DeviceBaudrate);

        // Migrate old wallpaper configuration to first page if needed
        if (config.Wallpaper != null && config.TouchButtonPages != null && config.TouchButtonPages.Count > 0)
        {
            if (config.TouchButtonPages[0].Wallpaper == null)
            {
                config.TouchButtonPages[0].Wallpaper = config.Wallpaper;
                config.TouchButtonPages[0].WallpaperOpacity = config.WallpaperOpacity;
                config.Wallpaper = null; // Clear old property
                config.WallpaperOpacity = 0;
            }
        }

        pageManager.OnTouchPageChanged += OnTouchPageChanged;
        folderNav.StateChanged += OnFolderStateChanged;

        // Subscribe to page property changes for wallpaper updates
        foreach (var page in config.TouchButtonPages)
        {
            page.PropertyChanged += TouchButtonPageOnPropertyChanged;
        }

        // Subscribe to collection changes to handle newly added pages
        config.TouchButtonPages.CollectionChanged += TouchButtonPagesOnCollectionChanged;

        config.SimpleButtons = await BuildSimpleButtons();

        if (config.RotaryButtonPages == null || config.RotaryButtonPages.Count == 0)
        {
            pageManager.AddRotaryButtonPage(true);
        }
        else
        {
            // Existing config Init always page 0.
            config.CurrentRotaryPageIndex = 0;
            pageManager.ApplyRotaryPage(config.CurrentRotaryPageIndex, true);
        }

        if (config.TouchButtonPages == null || config.TouchButtonPages.Count == 0)
        {
            await pageManager.AddTouchButtonPage(true);
        }
        else
        {
            var startupIndex = config.StartupTouchPageIndex;
            if (startupIndex < 0 || startupIndex >= config.TouchButtonPages.Count)
                startupIndex = 0;
            config.CurrentTouchPageIndex = startupIndex;
            await pageManager.ApplyTouchPage(config.CurrentTouchPageIndex, true);

            // With an existing config, we need to apply the item changed event to the current Touch Button Page
            foreach (var touchButton in config.CurrentTouchButtonPage.TouchButtons)
            {
                touchButton.ItemChanged += TouchItemChanged;
            }

            foreach (var touchButton in config.CurrentTouchButtonPage.TouchButtons)
            {
                await deviceService.Device.DrawTouchButton(touchButton, config, true, deviceService.Device.Columns);
            }
        }

        config.CurrentRotaryButtonPage.Selected = true;
        config.CurrentTouchButtonPage.Selected = true;

        config.PropertyChanged += ConfigOnPropertyChanged;

        await deviceService.Device.SetBrightness(config.Brightness / 100.0);

        InitButtonEvents();

        // Save the initial configuration.
        SaveConfig();

        await Task.CompletedTask;
    }

    private void InitButtonEvents()
    {
        var device = deviceService.Device;
        device.OnButton += OnSimpleButtonPress;
        device.OnTouch += OnTouchButtonPress;
        device.OnRotate += OnRotate;
    }

    private void OnSimpleButtonPress(object sender, ButtonEventArgs e)
    {
        if (e.EventType != Constants.ButtonEventType.BUTTON_DOWN)
            return;

        if (folderNav.IsActive)
        {
            // Side buttons are disabled in folder mode. Knob presses can still be
            // overridden by the active folder provider.
            if (TryGetRotaryIndex(e.ButtonId, out var rotaryIndex) &&
                folderNav.CurrentProvider?.RotaryOverrides is { } overrides &&
                overrides.TryGetValue(rotaryIndex, out var ov) &&
                ov.OnPress != null)
            {
                ov.OnPress().GetAwaiter().GetResult();
            }
            return;
        }

        var button = config.SimpleButtons.FirstOrDefault(b => b.Id == e.ButtonId);
        if (button != null)
        {
            if (_isDeviceOff && !button.EnableWhenOff) return;
            var wrapped = config.CurrentRotaryButtonPage?.SimpleButtonWrap?.Apply(button.Command) ?? button.Command;
            FireAndForget(wrapped);
            return;
        }

        if (!TryGetRotaryIndex(e.ButtonId, out var idx)) return;
        var page = config.RotaryButtonPages[config.CurrentRotaryPageIndex];
        if (page?.RotaryButtons == null || idx >= page.RotaryButtons.Count) return;
        var rotary = page.RotaryButtons[idx];
        if (_isDeviceOff && !rotary.EnableWhenOff) return;
        var cmd = rotary.Command;
        if (string.IsNullOrEmpty(cmd)) return;
        var wrappedRotary = page.KnobPressWrap?.Apply(cmd) ?? cmd;
        FireAndForget(wrappedRotary);
    }

    /// <summary>
    /// Runs the command off the serial-read thread. Critical: device-touching
    /// commands (SetBrightness, SetButtonColor, …) issue SendAsync calls whose
    /// completion is signalled by the read thread. If we awaited here we'd
    /// deadlock the very thread that needs to complete the await, and the
    /// device would appear disconnected after the first such command.
    /// </summary>
    private void FireAndForget(string command)
    {
        if (string.IsNullOrEmpty(command)) return;
        _ = Task.Run(async () =>
        {
            try { await commandService.ExecuteCommand(command); }
            catch (Exception ex) { Console.WriteLine($"Command failed ({command}): {ex.Message}"); }
        });
    }

    private void OnTouchButtonPress(object sender, TouchEventArgs e)
    {
        // Per-button override: native haptic skips these buttons entirely, so we
        // drive the legacy software Vibrate() pulse on both touch start and end.
        if (e.EventType == Constants.TouchEventType.TOUCH_END)
        {
            foreach (var touch in e.Touches)
            {
                var btn = config.CurrentTouchButtonPage?.TouchButtons?.FindByIndex(touch.Target.Key);
                if (btn != null && btn.VibrationEnabled)
                {
                    deviceService.Device.Vibrate(Constants.VibrationPattern.Off);
                    break;
                }
            }
            _activeTouchSlot = null;
            return;
        }

        if (e.EventType != Constants.TouchEventType.TOUCH_START)
            return;

        if (folderNav.IsActive)
        {
            foreach (var touch in e.Touches)
            {
                HandleFolderTouch(touch.Target.Key);
            }
            return;
        }

        foreach (var touch in e.Touches)
        {
            var slot = touch.Target.Key;

            if (config.TouchSlidingPreventionEnabled && _activeTouchSlot.HasValue)
                continue;

            var button = config.CurrentTouchButtonPage.TouchButtons.FindByIndex(slot);
            if (button == null) continue;
            if (_isDeviceOff && !button.EnableWhenOff) continue;

            _activeTouchSlot = slot;

            if (button.VibrationEnabled)
                deviceService.Device.Vibrate(button.VibrationPattern);

            if (config.TouchFeedbackEnabled)
                _ = ShowTouchFeedback(button);

            var wrapped = config.CurrentTouchButtonPage.TouchButtonWrap?.Apply(button.Command) ?? button.Command;
            FireAndForget(wrapped);
        }
    }

    /// <summary>
    /// Flashes a colored translucent overlay on the pressed touch slot for ~100ms,
    /// then restores the original rendered image. Fire-and-forget by design.
    /// </summary>
    private async Task ShowTouchFeedback(TouchButton button)
    {
        try
        {
            var device = deviceService.Device;
            if (device == null) return;

            var original = button.RenderedImage;
            // Use the original bitmap's dimensions so we cover Razer side panels
            // (60×270) and regular grid buttons (90×90) without special-casing.
            var width = original?.Width ?? 90;
            var height = original?.Height ?? 90;

            using var flash = new SkiaSharp.SKBitmap(width, height);
            using (var canvas = new SkiaSharp.SKCanvas(flash))
            {
                if (original != null) canvas.DrawBitmap(original, 0, 0);
                var c = config.TouchFeedbackColor;
                var alpha = (byte)Math.Clamp(255 * config.TouchFeedbackOpacity, 0, 255);
                using var paint = new SkiaSharp.SKPaint
                {
                    Color = new SkiaSharp.SKColor(c.R, c.G, c.B, alpha)
                };
                canvas.DrawRect(0, 0, width, height, paint);
            }

            await device.DrawTouchSlot(button.Index, flash);
            await Task.Delay(100);

            // Restore — if we have a cached original, draw it directly; otherwise
            // re-render the button through its normal path.
            if (original != null)
                await device.DrawTouchSlot(button.Index, original);
            else
                await device.DrawTouchButton(button, config, true, device.Columns);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Touch feedback failed: {ex.Message}");
        }
    }

    private void HandleFolderTouch(int slotIndex)
    {
        if (slotIndex < 0) return;

        if (slotIndex == FolderConstants.BackSlotIndex)
        {
            folderNav.NavigateBack().GetAwaiter().GetResult();
            return;
        }

        if (!folderNav.CurrentEntries.TryGetValue(slotIndex, out var entry))
            return; // empty slot — disabled

        if (entry.OpensFolder != null)
        {
            folderNav.OpenFolder(entry.OpensFolder).GetAwaiter().GetResult();
        }
        else if (entry.OnPress != null)
        {
            try { entry.OnPress().GetAwaiter().GetResult(); }
            catch (Exception ex) { Console.WriteLine($"Folder entry press failed: {ex.Message}"); }
        }
    }

    private void OnRotate(object sender, RotateEventArgs e)
    {
        if (folderNav.IsActive)
        {
            if (TryGetRotaryIndex(e.ButtonId, out var rotaryIndex) &&
                folderNav.CurrentProvider?.RotaryOverrides is { } overrides &&
                overrides.TryGetValue(rotaryIndex, out var ov))
            {
                var action = e.Delta < 0 ? ov.OnLeft : ov.OnRight;
                if (action != null)
                {
                    try { action().GetAwaiter().GetResult(); }
                    catch (Exception ex) { Console.WriteLine($"Folder rotary failed: {ex.Message}"); }
                }
            }
            return;
        }

        if (!TryGetRotaryIndex(e.ButtonId, out var idx)) return;
        var page = config.RotaryButtonPages[config.CurrentRotaryPageIndex];
        if (page?.RotaryButtons == null || idx >= page.RotaryButtons.Count) return;

        var btn = page.RotaryButtons[idx];
        if (_isDeviceOff && !btn.EnableWhenOff) return;
        var leftTurn = e.Delta < 0;
        var command = leftTurn ? btn.RotaryLeftCommand : btn.RotaryRightCommand;
        if (string.IsNullOrEmpty(command)) return;
        var wrap = leftTurn ? page.KnobLeftWrap : page.KnobRightWrap;
        var wrapped = wrap?.Apply(command) ?? command;
        FireAndForget(wrapped);
    }

    /// <summary>
    /// Maps the device knob id to its rotary-page slot index. Order matches the
    /// physical layout (left column top→bottom, then right column top→bottom),
    /// which is what both the Live S (slots 0–1) and the Razer Stream Controller
    /// (slots 0–5) consume.
    /// </summary>
    private static bool TryGetRotaryIndex(Constants.ButtonType id, out int index)
    {
        switch (id)
        {
            case Constants.ButtonType.KNOB_TL: index = 0; return true;
            case Constants.ButtonType.KNOB_CL: index = 1; return true;
            case Constants.ButtonType.KNOB_BL: index = 2; return true;
            case Constants.ButtonType.KNOB_TR: index = 3; return true;
            case Constants.ButtonType.KNOB_CR: index = 4; return true;
            case Constants.ButtonType.KNOB_BR: index = 5; return true;
            default: index = -1; return false;
        }
    }

    private async void OnFolderStateChanged()
    {
        try
        {
            var device = deviceService.Device;
            if (device == null) return;

            if (folderNav.IsActive)
            {
                for (var slot = 0; slot < FolderConstants.TotalSlots; slot++)
                {
                    SkiaSharp.SKBitmap bmp;
                    if (slot == FolderConstants.BackSlotIndex)
                    {
                        bmp = BitmapHelper.RenderFolderBackButton(config, slot, 90, 90, FolderConstants.Columns);
                    }
                    else if (folderNav.CurrentEntries.TryGetValue(slot, out var entry))
                    {
                        bmp = BitmapHelper.RenderFolderEntry(entry, config, slot, 90, 90, FolderConstants.Columns);
                    }
                    else
                    {
                        bmp = BitmapHelper.RenderEmptyFolderSlot(config, slot, 90, 90, FolderConstants.Columns);
                    }

                    await device.DrawTouchSlot(slot, bmp);
                }
            }
            else
            {
                // Folder mode left — restore the configured page.
                foreach (var touchButton in config.CurrentTouchButtonPage.TouchButtons)
                {
                    await device.DrawTouchButton(touchButton, config, true, device.Columns);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Folder redraw failed: {ex.Message}");
        }
    }

    private void OnTouchPageChanged(int oldIndex, int newIndex)
    {
        if (oldIndex >= 0 && oldIndex < config.TouchButtonPages.Count && config.TouchButtonPages[oldIndex] != null)
        {
            foreach (var touchButton in config.TouchButtonPages[oldIndex].TouchButtons)
            {
                touchButton.ItemChanged -= TouchItemChanged;
            }
        }

        if (newIndex >= 0 && newIndex < config.TouchButtonPages.Count && config.TouchButtonPages[newIndex] != null)
        {
            foreach (var touchButton in config.TouchButtonPages[newIndex].TouchButtons)
            {
                touchButton.ItemChanged += TouchItemChanged;
            }
        }
    }

    private void TouchButtonPagesOnCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // Subscribe to property changes for newly added pages
        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is TouchButtonPage page)
                {
                    page.PropertyChanged += TouchButtonPageOnPropertyChanged;
                }
            }
        }

        // Unsubscribe from property changes for removed pages
        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is TouchButtonPage page)
                {
                    page.PropertyChanged -= TouchButtonPageOnPropertyChanged;
                }
            }
        }
    }

    private async void TouchButtonPageOnPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (sender is not TouchButtonPage page) return;

        // Only redraw if this is the current page and wallpaper properties changed
        if (page != config.CurrentTouchButtonPage) return;

        _propertyChangedCts?.Cancel();
        _propertyChangedCts = new CancellationTokenSource();
        var token = _propertyChangedCts.Token;

        try
        {
            switch (e.PropertyName)
            {
                case nameof(TouchButtonPage.Wallpaper):
                case nameof(TouchButtonPage.WallpaperOpacity):
                    await Task.Delay(100, token); // Debounce
                    foreach (var touchButton in config.CurrentTouchButtonPage.TouchButtons)
                    {
                        await deviceService.Device.DrawTouchButton(touchButton, config, true, deviceService.Device.Columns);
                        await Task.Delay(0, token);
                    }
                    break;
            }
        }
        catch (TaskCanceledException)
        {
            // ignore canceled Tasks
        }
    }

    private async void TouchItemChanged(object sender, EventArgs e)
    {
        if (sender is not TouchButton item) return;

        // Folder mode owns the touch display — suppress per-button redraws (e.g. from
        // DynamicTextManager) so they don't paint over the folder view. When the folder
        // exits, OnFolderStateChanged redraws every slot from the current state.
        if (folderNav.IsActive) return;

        var button = config.CurrentTouchButtonPage.TouchButtons.FirstOrDefault(b => b.Index == item.Index);

        if (button == null) return;

        await deviceService.Device.DrawTouchButton(button, config, true, deviceService.Device.Columns);
    }

    /// <summary>
    /// Builds the SimpleButton array sized to the active device's physical button count.
    /// The first four slots get the page-navigation defaults that existed for the Live S;
    /// any additional slots (e.g. Razer's BUTTON4–BUTTON7) are created blank for the
    /// user to assign — preserves saved bindings via SimpleButtonExtensions.FindById.
    /// </summary>
    private async Task<SimpleButton[]> BuildSimpleButtons()
    {
        var defaults = new (Constants.ButtonType Id, string Cmd)[]
        {
            (Constants.ButtonType.BUTTON0, "System.PreviousPage"),
            (Constants.ButtonType.BUTTON1, "System.PreviousRotaryPage"),
            (Constants.ButtonType.BUTTON2, "System.NextRotaryPage"),
            (Constants.ButtonType.BUTTON3, "System.NextPage"),
            (Constants.ButtonType.BUTTON4, null),
            (Constants.ButtonType.BUTTON5, null),
            (Constants.ButtonType.BUTTON6, null),
            (Constants.ButtonType.BUTTON7, null)
        };

        var device = deviceService.Device;
        var count = device.Buttons?.Length ?? 0;
        var result = new SimpleButton[count];
        for (var i = 0; i < count && i < defaults.Length; i++)
        {
            result[i] = await CreateSimpleButton(defaults[i].Id, Avalonia.Media.Colors.Blue, defaults[i].Cmd ?? string.Empty);
        }
        return result;
    }

    private async Task<SimpleButton> CreateSimpleButton(Constants.ButtonType id, Avalonia.Media.Color color,
        string command)
    {
        var button = config.SimpleButtons.FindById(id) ?? new SimpleButton
        {
            Id = id,
            Command = command,
            ButtonColor = color
        };

        Avalonia.Threading.Dispatcher.UIThread.Invoke(() =>
        {
            button.RenderedImage = BitmapHelper.RenderSimpleButtonImage(button, 90, 90);
        });

        button.ItemChanged += SimpleButtonChanged;

        await deviceService.Device.SetButtonColor(id, button.ButtonColor);

        return button;
    }

    private async void SimpleButtonChanged(object sender, EventArgs e)
    {
        if (sender is not SimpleButton button) return;

        button.RenderedImage = BitmapHelper.RenderSimpleButtonImage(button, 90, 90);
        await deviceService.Device.SetButtonColor(button.Id, button.ButtonColor);
    }

    private readonly SemaphoreSlim _saveSemaphore = new(1, 1);

    // Fire-and-forget save. The semaphore serializes concurrent calls so the
    // temp-file rename stays atomic.
    public void SaveConfig()
    {
        _ = SaveConfigAsync();
    }

    public async Task SaveConfigAsync()
    {
        await _saveSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            // Marshal serialization onto the UI thread: the config tree contains
            // ObservableCollections (pages → buttons → layers) that the UI may
            // mutate at any time. Iterating them off-thread races and throws
            // "Collection was modified". With the layer-based config we no
            // longer embed bitmaps, so the JSON write itself is cheap enough to
            // run on the UI thread without a perceptible hitch.
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                () => configService.SaveConfig(config, _configPath));

            // Snapshot referenced asset paths on the UI thread, then perform
            // the actual filesystem cleanup off-thread.
            var referenced = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                () => CollectReferencedAssetPaths().ToList());
            await Task.Run(() => assetService.Cleanup(referenced)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SaveConfig failed: {ex.Message}");
        }
        finally
        {
            _saveSemaphore.Release();
        }
    }

    private IEnumerable<string> CollectReferencedAssetPaths()
    {
        if (config.TouchButtonPages == null) yield break;

        foreach (var page in config.TouchButtonPages)
        {
            if (page?.TouchButtons == null) continue;
            foreach (var button in page.TouchButtons)
            {
                if (button?.Layers == null) continue;
                foreach (var layer in button.Layers)
                {
                    if (layer is ImageLayer img && !string.IsNullOrWhiteSpace(img.AssetRelativePath))
                        yield return img.AssetRelativePath;
                }
            }
        }
    }

    private CancellationTokenSource _propertyChangedCts;
    
    private async void ConfigOnPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        _propertyChangedCts?.Cancel();
        _propertyChangedCts = new CancellationTokenSource();
        var token = _propertyChangedCts.Token;

        try
        {
            switch (e.PropertyName)
            {
                case nameof(LoupedeckConfig.Brightness):
                    await Task.Delay(100, token); // Debounce
                    await deviceService.Device.SetBrightness(config.Brightness / 100.0);
                    break;
            }
        }
        catch (TaskCanceledException)
        {
            // ignore canceled Tasks
        }
    }
}