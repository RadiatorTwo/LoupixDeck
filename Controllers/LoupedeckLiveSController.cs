using System.ComponentModel;
using LoupixDeck.LoupedeckDevice;
using LoupixDeck.Models;
using LoupixDeck.Models.Extensions;
using LoupixDeck.Models.Layers;
using LoupixDeck.PluginSdk;
using LoupixDeck.Registry;
using LoupixDeck.Services;
using LoupixDeck.Services.FolderNavigation;
using LoupixDeck.Services.Plugins;
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
public partial class LoupedeckLiveSController(
    IDeviceService deviceService,
    ICommandService commandService,
    IPageManager pageManager,
    IConfigService configService,
    IFolderNavigationService folderNav,
    IExclusiveModeService exclusiveMode,
    ISideStripProviderRegistry sideStripRegistry,
    IAssetService assetService,
    INativeHapticService nativeHapticService,
    LoupedeckConfig config,
    DeviceRegistry.DeviceInfo deviceInfo,
    ResolvedDevice resolved) : IDeviceController
{
    private readonly string _configPath = deviceInfo != null
        ? FileDialogHelper.GetConfigPath(deviceInfo, resolved?.Serial)
        : FileDialogHelper.GetConfigPath("config.json");

    public IPageManager PageManager => pageManager;

    public LoupedeckConfig Config => config;

    private volatile bool _isDeviceOff;
    public bool IsDeviceOff => _isDeviceOff;

    // Tracks the slot index of the currently active touch contact. Set on the
    // first TOUCH_START of a finger-down sequence, cleared on TOUCH_END.
    private int? _activeTouchSlot;

    // ───────── Plugin-override side strips (Razer) ─────────
    // Live session driving each side strip ([0]=Left, [1]=Right), or null. Created
    // when the side's current page is a PluginOverride page with a resolvable provider,
    // disposed when navigating away / the device is taken over. The provider+page the
    // session was created for are tracked so an idempotent refresh doesn't recreate it,
    // while a real page or binding change does.
    private readonly ISideStripSession[] _stripSession = new ISideStripSession[2];
    private readonly ISideStripProvider[] _stripProvider = new ISideStripProvider[2];
    private readonly RotaryButtonPage[] _stripPage = new RotaryButtonPage[2];
    private readonly SemaphoreSlim[] _stripRedrawGate = [new(1, 1), new(1, 1)];
    private readonly long[] _stripRedrawGen = new long[2];
    private readonly long[] _stripDrawnGen = new long[2];
    private readonly long[] _stripLastDrawTick = new long[2];
    private const int StripMinRedrawMs = 33; // ~30 fps floor per strip

    // ───────── Segmented-mode per-segment providers (Razer) ─────────
    // In segmented strip mode a provider may render individual segments (e.g. an audio dial's
    // volume bar) while the host draws the other dials' labels. Kept separate from the
    // override session above so swipe stays default paging in segmented mode (only tap is
    // routed to the segment session). Rebuilt when the page object or the rotaries' command
    // bindings change (an editor edit), detected via _segmentBindingSig.
    private readonly ISideStripSession[] _segmentSession = new ISideStripSession[2];
    private readonly ISegmentStripProvider[] _segmentProvider = new ISegmentStripProvider[2];
    private readonly RotaryButtonPage[] _segmentPage = new RotaryButtonPage[2];
    private readonly string[] _segmentBindingSig = new string[2];

    private static int SideIndex(RotarySide side) => side == RotarySide.Right ? 1 : 0;

    public async Task ClearDeviceState()
    {
        if (_isDeviceOff) return;
        _isDeviceOff = true;
        // Stop any plugin-strip providers so their timers don't churn while off.
        DetachAllSideStripProviders();
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
            await RedrawSideStrips();
            // Re-program firmware haptic from the persisted config.
            nativeHapticService.Apply();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"RestoreDeviceState failed: {ex.Message}");
        }
    }

    public Task ToggleDeviceState() => _isDeviceOff ? RestoreDeviceState() : ClearDeviceState();

    public async Task RedrawCurrentTouchPage()
    {
        // No-op while something else owns the screen — the owner repaints when it
        // releases (device-off → RestoreDeviceState, folder/exclusive → their exit
        // handlers), so painting here would fight them.
        if (_isDeviceOff || folderNav.IsActive || exclusiveMode.IsActive)
            return;

        var device = deviceService.Device;
        if (device == null || config.CurrentTouchButtonPage?.TouchButtons == null)
            return;

        foreach (var tb in config.CurrentTouchButtonPage.TouchButtons)
        {
            await device.DrawTouchButton(tb, config, true, device.Columns);
        }

        await RedrawSideStrips();
    }

    public async Task Initialize(string port = null, int baudrate = 0)
    {
        if (port != null)
            Config.DevicePort = port;

        if (baudrate > 0)
            Config.DeviceBaudrate = baudrate;

        // Auto-detect path never sets baudrate, so the config would otherwise
        // persist as 0 and Settings would show "0" even though the device runs
        // on the 115200 fallback inside LoupedeckDevice.
        if (Config.DeviceBaudrate <= 0)
            Config.DeviceBaudrate = 115200;

        // Stamp the active device's VID/PID (and serial) into the config so
        // subsequent launches load the right per-device file via ActiveDeviceResolver.
        if (deviceInfo != null)
        {
            Config.DeviceVid = deviceInfo.VendorId;
            Config.DevicePid = deviceInfo.ProductId;
        }

        if (!string.IsNullOrEmpty(resolved?.Serial))
            Config.DeviceSerial = resolved.Serial;

        // Re-detect the current port. The OS may have assigned a different
        // COM/ttyACM number since the last save (USB reconnect, suspend wake-up,
        // hub change). When the config knows this unit's serial, match on it first
        // so two identical devices can't steal each other's port; fall back to
        // VID/PID otherwise. Skip when the user just picked a port explicitly via
        // InitSetup — that's an authoritative override.
        if (port == null && !string.IsNullOrEmpty(Config.DeviceVid) && !string.IsNullOrEmpty(Config.DevicePid))
        {
            try
            {
                var candidates = SerialDeviceHelper.ListSerialUsbDevices()
                    .Where(d =>
                        string.Equals(d.Vid, Config.DeviceVid, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(d.Pid, Config.DevicePid, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var current = (!string.IsNullOrEmpty(Config.DeviceSerial)
                                  ? candidates.FirstOrDefault(d =>
                                      string.Equals(d.NormalizedSerial, Config.DeviceSerial, StringComparison.OrdinalIgnoreCase))
                                  : null)
                              ?? candidates.FirstOrDefault();

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

        // (The legacy root-level → page-0 wallpaper migration now lives in
        //  WallpaperAssetMigrator, which also moves wallpapers into the asset folder.)

        pageManager.OnTouchPageChanged += OnTouchPageChanged;
        folderNav.StateChanged += OnFolderStateChanged;
        exclusiveMode.StateChanged += OnExclusiveStateChanged;

        // Subscribe to page property changes for wallpaper updates
        foreach (var page in config.TouchButtonPages)
        {
            page.PropertyChanged += TouchButtonPageOnPropertyChanged;
        }

        // Subscribe to collection changes to handle newly added pages
        config.TouchButtonPages.CollectionChanged += TouchButtonPagesOnCollectionChanged;

        config.SimpleButtons = await BuildSimpleButtons();

        InitializeRotaryPages();

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

        // Rotary selection is already set by InitializeRotaryPages (per side on
        // side-strip devices, where CurrentRotaryButtonPage/Both is intentionally null).
        if (config.CurrentRotaryButtonPage != null)
            config.CurrentRotaryButtonPage.Selected = true;
        config.CurrentTouchButtonPage.Selected = true;

        config.PropertyChanged += ConfigOnPropertyChanged;

        await deviceService.Device.SetBrightness(config.Brightness / 100.0);

        // Re-apply the simple-button LED colours now that the device is fully initialised.
        // BUTTON0 is the device's boot status LED: the firmware holds it green during
        // start-up and only releases LED control after init (brightness/first draw), so
        // the colour set early in BuildSimpleButtons gets clobbered. Re-sending here (after
        // the firmware has released it) makes BUTTON0 honour its configured colour like the
        // others. See the bottom-left-button-always-green investigation.
        await ReapplySimpleButtonColors();

        // Paint the initial segmented rotary labels onto the side strips (Razer).
        await RedrawSideStrips();

        InitButtonEvents();

        // Save the initial configuration.
        SaveConfig();

        await Task.CompletedTask;
    }

    private async Task ReapplySimpleButtonColors()
    {
        if (config.SimpleButtons == null) return;

        var device = deviceService.Device;
        if (device == null) return;

        foreach (var button in config.SimpleButtons)
        {
            if (button == null) continue;
            await device.SetButtonColor(button.Id, button.ButtonColor);
        }
    }

    private void InitButtonEvents()
    {
        var device = deviceService.Device;
        device.OnButton += OnSimpleButtonPress;
        device.OnTouch += OnTouchButtonPress;
        device.OnRotate += OnRotate;
        device.OnSwipe += OnSwipe;

        // Repaint the affected side strip whenever its rotary page changes.
        pageManager.OnRotaryPageChanged += OnRotaryPageChanged;
    }

    /// <summary>
    /// Sets up the rotary pages for the active device. Devices with side strips
    /// (Razer) page their two dial columns independently, so each side gets its own
    /// page set; other devices keep the single shared page list.
    /// </summary>
    private void InitializeRotaryPages()
    {
        if (deviceService.Device?.HasSideStrips == true)
        {
            foreach (var side in new[] { RotarySide.Left, RotarySide.Right })
            {
                if (pageManager.GetRotaryPages(side).Count == 0)
                    pageManager.AddRotaryButtonPage(side, true);
                else
                    pageManager.ApplyRotaryPage(side, 0, true);
            }
            return;
        }

        if (config.RotaryButtonPages == null || config.RotaryButtonPages.Count == 0)
            pageManager.AddRotaryButtonPage(RotarySide.Both, true);
        else
            pageManager.ApplyRotaryPage(RotarySide.Both, 0, true);
    }

    private static RotarySide ToRotarySide(SideStrip strip) =>
        strip == SideStrip.Left ? RotarySide.Left : RotarySide.Right;

    /// <summary>
    /// Maps a global knob index (0–2 left, 3–5 right) to its dial column and the
    /// per-side 0-based index used inside a side page.
    /// </summary>
    private static (RotarySide Side, int LocalIndex) ResolveRotary(int globalIndex) =>
        globalIndex < 3
            ? (RotarySide.Left, globalIndex)
            : (RotarySide.Right, globalIndex - 3);

    /// <summary>
    /// Resolves the rotary button for a global knob index (0–5) against the active
    /// rotary page. On side-strip devices this picks the matching column's current
    /// page and the per-side local index; otherwise the shared page and the index
    /// as-is. Returns null when no page/button is available.
    /// </summary>
    private (RotaryButtonPage Page, RotaryButton Button)? ResolveRotaryButton(int globalIndex)
    {
        RotaryButtonPage page;
        int localIndex;

        if (deviceService.Device?.HasSideStrips == true)
        {
            var (side, local) = ResolveRotary(globalIndex);
            page = pageManager.GetCurrentRotaryPage(side);
            localIndex = local;
        }
        else
        {
            page = config.CurrentRotaryButtonPage;
            localIndex = globalIndex;
        }

        if (page?.RotaryButtons == null || localIndex < 0 || localIndex >= page.RotaryButtons.Count)
            return null;

        return (page, page.RotaryButtons[localIndex]);
    }

    private void OnSwipe(object sender, SwipeEventArgs e)
    {
        if (_isDeviceOff || exclusiveMode.IsActive || folderNav.IsActive)
            return;

        var side = ToRotarySide(e.Side);

        // A plugin-override strip owns its gestures: the provider decides whether to
        // page (via the context callbacks) or consume the swipe.
        if (IsPluginStripActive(side, out var session))
        {
            var direction = e.Direction == SwipeDirection.Up ? StripSwipeDirection.Up : StripSwipeDirection.Down;
            try { session.OnStripSwiped(direction); }
            catch (Exception ex) { Console.WriteLine($"Side-strip session swipe failed: {ex.Message}"); }
            return;
        }

        // Non-plugin strips: the finger-follow drag state machine (OnTouchButtonPress →
        // HandleStripDragEnd) owns paging and commits distance-based on release. Ignore
        // the device-layer swipe here so the page isn't changed twice (OnSwipe fires
        // just before the TOUCH_END that the drag machine consumes).
        if (StripAnimationApplicable(side))
            return;

        if (e.Direction == SwipeDirection.Up)
            pageManager.NextRotaryPage(side);
        else
            pageManager.PreviousRotaryPage(side);
    }

    private async void OnRotaryPageChanged(RotarySide side, int previousIndex, int newIndex)
    {
        // Only side-strip devices render rotary labels onto a strip.
        if (deviceService.Device?.HasSideStrips != true || side == RotarySide.Both)
            return;

        if (_isDeviceOff || exclusiveMode.IsActive || folderNav.IsActive)
            return;

        try
        {
            EnsureStripAttachment(side);
            EnsureSegmentAttachment(side);
            await DrawSideStrip(side);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Side-strip redraw failed ({side}): {ex.Message}");
        }
    }

    /// <summary>
    /// Renders the segmented label strip for one dial column (3 stacked 60×90 knob
    /// labels) and pushes it to the matching side panel region of the unified display.
    /// </summary>
    private async Task DrawSideStrip(RotarySide side)
    {
        var device = deviceService.Device;
        if (device is not LoupedeckDevice.Device.RazerStreamControllerDevice razer)
            return;

        var page = pageManager.GetCurrentRotaryPage(side);
        if (page == null) return;

        var slotIndex = side == RotarySide.Left
            ? LoupedeckDevice.Device.RazerStreamControllerDevice.LeftSideIndex
            : LoupedeckDevice.Device.RazerStreamControllerDevice.RightSideIndex;

        var strip = RenderStripFor(page, side, useSessions: true);

        // Mirror the strip onto the on-screen mask: the side-panel buttons bind to
        // their TouchButton.RenderedImage. The setter owns the bitmap's lifetime
        // (deferred dispose), so don't dispose it here — it's also read by the device
        // push below and held by the UI binding.
        var slotButton = config.CurrentTouchButtonPage?.TouchButtons?.FindByIndex(slotIndex);
        if (slotButton != null)
            slotButton.RenderedImage = strip;

        await razer.DrawTouchSlot(slotIndex, strip);
    }

    /// <summary>
    /// Renders a single 60×270 strip bitmap for the given page and side, per its
    /// <see cref="StripMode"/>. <paramref name="useSessions"/> drives whether the live
    /// plugin/segment sessions (bound to the side's *current* page) contribute: the
    /// authoritative current-page render passes true; transient neighbour frames for a
    /// swipe animation pass false and render the page's own content as plain labels.
    /// </summary>
    private SkiaSharp.SKBitmap RenderStripFor(RotaryButtonPage page, RotarySide side, bool useSessions)
    {
        // PluginOverride: a plugin provider renders the strip; FreeDraw: the page's
        // editable canvas; Segmented (default): the three adjacent dial labels.
        return page.StripMode switch
        {
            StripMode.PluginOverride => useSessions
                ? RenderPluginStripOrFallback(page, side)
                : BitmapHelper.RenderRotaryStrip(page, config, 60, 270, side),
            StripMode.FreeDraw => BitmapHelper.RenderStripCanvas(page.StripCanvas, config, 60, 270, side),
            _ => useSessions
                ? BitmapHelper.RenderRotaryStrip(page, config, 60, 270, side,
                    (i, rc) => (_segmentSession[SideIndex(side)] as ISegmentStripSession)?.RenderSegment(i, rc) ?? false)
                : BitmapHelper.RenderRotaryStrip(page, config, 60, 270, side)
        };
    }

    /// <summary>Re-evaluates plugin-override attachment for both strips, then repaints
    /// them (no-op on devices without side strips).</summary>
    private async Task RedrawSideStrips()
    {
        if (deviceService.Device?.HasSideStrips != true) return;
        EnsureStripAttachment(RotarySide.Left);
        EnsureStripAttachment(RotarySide.Right);
        EnsureSegmentAttachment(RotarySide.Left);
        EnsureSegmentAttachment(RotarySide.Right);
        await DrawSideStrip(RotarySide.Left);
        await DrawSideStrip(RotarySide.Right);
    }

    /// <summary>Repaints a single side strip — public entry for the UI after the user
    /// edits that side's strip (free-draw canvas or plugin-override binding). Re-evaluates
    /// plugin attachment first so a just-changed mode/provider takes effect immediately.
    /// No-op on devices without side strips.</summary>
    public Task RefreshSideStrip(RotarySide side)
    {
        EnsureStripAttachment(side);
        EnsureSegmentAttachment(side);
        return DrawSideStrip(side);
    }

    /// <inheritdoc/>
    public async Task RefreshSideStrips()
    {
        if (_isDeviceOff || folderNav.IsActive || exclusiveMode.IsActive) return;
        await RedrawSideStrips();
    }

    /// <inheritdoc/>
    public void DetachAllSideStripProviders()
    {
        ResetStripDrags();
        DetachStripAt(0);
        DetachStripAt(1);
        DetachSegmentAt(0);
        DetachSegmentAt(1);
    }

    /// <summary>
    /// Renders a plugin-override strip: the attached provider draws the whole 60×270 strip onto a
    /// host canvas. Falls back to the segmented dial labels when no provider is attached or the
    /// provider declines (returns false) / throws.
    /// </summary>
    private SkiaSharp.SKBitmap RenderPluginStripOrFallback(RotaryButtonPage page, RotarySide side)
    {
        var session = _stripSession[SideIndex(side)];
        if (session != null)
        {
            // The session draws with SkiaSharp via the canvas; serialize with all other Skia work
            // so a plugin frame can't race the host's render pipeline (caches aren't thread-safe).
            var bitmap = new SkiaSharp.SKBitmap(60, 270);
            try
            {
                lock (SkiaRenderGate.Sync)
                {
                    using var canvas = new SkiaSharp.SKCanvas(bitmap);
                    var rc = new SkiaRenderCanvas(canvas, 60, 270);
                    if (session.RenderStrip(rc))
                    {
                        canvas.Flush();
                        return bitmap;
                    }
                }
                bitmap.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Side-strip session RenderStrip failed ({side}): {ex.Message}");
                bitmap.Dispose();
            }
        }

        // Unbound / orphaned id / declined / failed → segmented labels.
        return BitmapHelper.RenderRotaryStrip(page, config, 60, 270, side);
    }

    /// <summary>
    /// Creates/disposes the plugin-override session for one side so it matches the side's
    /// current rotary page: a PluginOverride page with a resolvable provider gets a fresh
    /// session (with that page's dial bindings in the context); any other state disposes
    /// the previous one. Recreates when the page object or bound provider changes — so
    /// navigating between two plugin pages rebinds the session to the new page's dials —
    /// but an idempotent refresh on the same page/provider is a no-op.
    /// </summary>
    private void EnsureStripAttachment(RotarySide side)
    {
        if (deviceService.Device?.HasSideStrips != true || side == RotarySide.Both) return;

        var idx = SideIndex(side);
        var page = pageManager.GetCurrentRotaryPage(side);

        ISideStripProvider desired = null;
        if (page is { StripMode: StripMode.PluginOverride })
            desired = sideStripRegistry.Get(page.StripPluginId);

        // Nothing changed (same page object, same resolved provider) → keep the session.
        if (ReferenceEquals(_stripPage[idx], page) && ReferenceEquals(_stripProvider[idx], desired))
            return;

        DetachStripAt(idx);
        _stripPage[idx] = page;
        _stripProvider[idx] = desired;
        if (desired == null) return;

        var context = new SideStripContext
        {
            Side = side == RotarySide.Right ? StripSide.Right : StripSide.Left,
            Width = 60,
            Height = 270,
            Rotaries = BuildStripRotaries(page),
            RequestNextPage = () => pageManager.NextRotaryPage(side),
            RequestPreviousPage = () => pageManager.PreviousRotaryPage(side)
        };

        try
        {
            var session = desired.CreateSession(context);
            if (session == null) return;
            _stripSession[idx] = session;
            session.StripChanged += OnStripSessionChanged;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Side-strip provider '{desired.Id}' CreateSession failed: {ex.Message}");
        }
    }

    /// <summary>Maps a side page's dials to the SDK's rotary context (top-to-bottom).</summary>
    private static IReadOnlyList<SideStripRotary> BuildStripRotaries(RotaryButtonPage page)
    {
        var rotaries = page.RotaryButtons;
        if (rotaries == null || rotaries.Count == 0) return Array.Empty<SideStripRotary>();

        var list = new List<SideStripRotary>(rotaries.Count);
        for (var i = 0; i < rotaries.Count; i++)
        {
            var r = rotaries[i];
            list.Add(new SideStripRotary
            {
                Index = i,
                Label = r.DisplayText ?? string.Empty,
                LeftCommand = r.RotaryLeftCommand ?? string.Empty,
                RightCommand = r.RotaryRightCommand ?? string.Empty,
                PressCommand = r.Command ?? string.Empty
            });
        }
        return list;
    }

    private void DetachStripAt(int idx)
    {
        var session = _stripSession[idx];
        _stripSession[idx] = null;
        _stripProvider[idx] = null;
        _stripPage[idx] = null;
        if (session == null) return;

        session.StripChanged -= OnStripSessionChanged;
        try { session.Dispose(); }
        catch (Exception ex) { Console.WriteLine($"Side-strip session dispose failed: {ex.Message}"); }
    }

    /// <summary>
    /// Creates/disposes the per-segment session for one side in <see cref="StripMode.Segmented"/>
    /// mode: when the side's current page is segmented and a segment-capable provider is loaded,
    /// a session is attached so individual segments (e.g. an audio dial's volume bar) can be
    /// plugin-rendered while the host draws the other dials' labels. Rebuilt when the page object,
    /// the resolved provider, or the rotaries' command bindings change (an editor edit); any
    /// non-segmented page detaches it. Distinct from the override session so swipe stays default
    /// paging in segmented mode.
    /// </summary>
    private void EnsureSegmentAttachment(RotarySide side)
    {
        if (deviceService.Device?.HasSideStrips != true || side == RotarySide.Both) return;

        var idx = SideIndex(side);
        var page = pageManager.GetCurrentRotaryPage(side);

        ISegmentStripProvider desired = null;
        if (page is { StripMode: StripMode.Segmented })
            desired = sideStripRegistry.Providers.OfType<ISegmentStripProvider>().FirstOrDefault();

        // A binding signature detects an editor edit (commands changed on the same page object).
        var sig = desired == null ? null : BuildBindingSignature(page);

        if (ReferenceEquals(_segmentPage[idx], page)
            && ReferenceEquals(_segmentProvider[idx], desired)
            && _segmentBindingSig[idx] == sig)
            return;

        DetachSegmentAt(idx);
        _segmentPage[idx] = page;
        _segmentProvider[idx] = desired;
        _segmentBindingSig[idx] = sig;
        if (desired == null) return;

        var context = new SideStripContext
        {
            Side = side == RotarySide.Right ? StripSide.Right : StripSide.Left,
            Width = 60,
            Height = 270,
            Rotaries = BuildStripRotaries(page),
            RequestNextPage = () => pageManager.NextRotaryPage(side),
            RequestPreviousPage = () => pageManager.PreviousRotaryPage(side)
        };

        try
        {
            var session = desired.CreateSession(context);
            if (session == null) return;
            _segmentSession[idx] = session;
            session.StripChanged += OnStripSessionChanged;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Segment-strip provider '{desired.Id}' CreateSession failed: {ex.Message}");
        }
    }

    /// <summary>Concatenates the 3 dials' Left/Right/Press command strings so a binding change
    /// (e.g. assigning an audio command in the editor) is detectable without a page-object swap.</summary>
    private static string BuildBindingSignature(RotaryButtonPage page)
    {
        var rotaries = page?.RotaryButtons;
        if (rotaries == null || rotaries.Count == 0) return string.Empty;

        var parts = new List<string>(rotaries.Count * 3);
        foreach (var r in rotaries)
        {
            parts.Add(r?.RotaryLeftCommand ?? string.Empty);
            parts.Add(r?.RotaryRightCommand ?? string.Empty);
            parts.Add(r?.Command ?? string.Empty);
        }
        return string.Join("|", parts);
    }

    private void DetachSegmentAt(int idx)
    {
        var session = _segmentSession[idx];
        _segmentSession[idx] = null;
        _segmentProvider[idx] = null;
        _segmentPage[idx] = null;
        _segmentBindingSig[idx] = null;
        if (session == null) return;

        session.StripChanged -= OnStripSessionChanged;
        try { session.Dispose(); }
        catch (Exception ex) { Console.WriteLine($"Segment-strip session dispose failed: {ex.Message}"); }
    }

    /// <summary>True when the side's current page is PluginOverride and a session is
    /// active; outputs the session for input routing.</summary>
    private bool IsPluginStripActive(RotarySide side, out ISideStripSession session)
    {
        session = null;
        if (deviceService.Device?.HasSideStrips != true || side == RotarySide.Both) return false;
        var page = pageManager.GetCurrentRotaryPage(side);
        if (page is not { StripMode: StripMode.PluginOverride }) return false;
        session = _stripSession[SideIndex(side)];
        return session != null;
    }

    /// <summary>StripChanged handler — coalesced, rate-limited per-side redraw. May be
    /// invoked from a plugin background thread.</summary>
    private void OnStripSessionChanged(object sender, EventArgs e)
    {
        if (sender is not ISideStripSession session) return;
        var idx = ReferenceEquals(_stripSession[0], session) ? 0
                : ReferenceEquals(_stripSession[1], session) ? 1
                : ReferenceEquals(_segmentSession[0], session) ? 0
                : ReferenceEquals(_segmentSession[1], session) ? 1 : -1;
        if (idx < 0) return;
        // A provider frame mid-swipe would draw the page flat at offset 0 and fight the
        // slide; the drag owns the strip until it settles, then the next change repaints.
        if (IsStripDragBusy(idx)) return;
        _ = RedrawStripCoalesced(idx == 0 ? RotarySide.Left : RotarySide.Right, idx);
    }

    private async Task RedrawStripCoalesced(RotarySide side, int idx)
    {
        var requested = Interlocked.Increment(ref _stripRedrawGen[idx]);
        await _stripRedrawGate[idx].WaitAsync();
        try
        {
            // A later request already rendered at least this fresh (RenderStrip reads
            // live provider state, so the newest frame is always what gets drawn).
            if (Interlocked.Read(ref _stripDrawnGen[idx]) >= requested) return;
            if (_isDeviceOff || folderNav.IsActive || exclusiveMode.IsActive) return;

            var since = Environment.TickCount64 - _stripLastDrawTick[idx];
            if (since < StripMinRedrawMs)
                await Task.Delay((int)(StripMinRedrawMs - since));

            var snapshot = Interlocked.Read(ref _stripRedrawGen[idx]);
            await DrawSideStrip(side);
            Interlocked.Exchange(ref _stripDrawnGen[idx], snapshot);
            _stripLastDrawTick[idx] = Environment.TickCount64;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Side-strip provider redraw failed ({side}): {ex.Message}");
        }
        finally
        {
            _stripRedrawGate[idx].Release();
        }
    }

    /// <summary>True for the Razer side-strip slots, which are driven by rotary labels.</summary>
    private bool IsSideStripSlot(int slot) =>
        deviceService.Device?.HasSideStrips == true &&
        slot is LoupedeckDevice.Device.RazerStreamControllerDevice.LeftSideIndex
             or LoupedeckDevice.Device.RazerStreamControllerDevice.RightSideIndex;

    private void OnSimpleButtonPress(object sender, ButtonEventArgs e)
    {
        if (e.EventType != Constants.ButtonEventType.BUTTON_DOWN)
            return;

        if (exclusiveMode.IsActive)
        {
            // Exclusive provider receives the raw 0-based button index. Rotary
            // presses are forwarded through OnRotaryPressed; everything else is
            // a simple button.
            if (TryGetRotaryIndex(e.ButtonId, out var rIdx))
            {
                try { exclusiveMode.Current?.OnRotaryPressed(rIdx); }
                catch (Exception ex) { Console.WriteLine($"Exclusive rotary press: {ex.Message}"); }
            }
            else
            {
                var sbIdx = Array.FindIndex(config.SimpleButtons ?? Array.Empty<SimpleButton>(),
                    b => b != null && b.Id == e.ButtonId);
                if (sbIdx >= 0)
                {
                    try { exclusiveMode.Current?.OnSimpleButtonPressed(sbIdx); }
                    catch (Exception ex) { Console.WriteLine($"Exclusive button press: {ex.Message}"); }
                }
            }
            return;
        }

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
            FireAndForget(wrapped, ButtonTargets.SimpleButton);
            return;
        }

        if (!TryGetRotaryIndex(e.ButtonId, out var idx)) return;
        var resolved = ResolveRotaryButton(idx);
        if (resolved == null) return;
        var (page, rotary) = resolved.Value;
        if (_isDeviceOff && !rotary.EnableWhenOff) return;
        var cmd = rotary.Command;
        if (string.IsNullOrEmpty(cmd)) return;
        var wrappedRotary = page.KnobPressWrap?.Apply(cmd) ?? cmd;
        FireAndForget(wrappedRotary, ButtonTargets.RotaryEncoder, idx);
    }

    /// <summary>
    /// Runs the command off the serial-read thread. Critical: device-touching
    /// commands (SetBrightness, SetButtonColor, …) issue SendAsync calls whose
    /// completion is signalled by the read thread. If we awaited here we'd
    /// deadlock the very thread that needs to complete the await, and the
    /// device would appear disconnected after the first such command.
    /// </summary>
    private void FireAndForget(string command, ButtonTargets target, int? sourceIndex = null)
    {
        if (string.IsNullOrEmpty(command)) return;
        _ = Task.Run(async () =>
        {
            try { await commandService.ExecuteCommand(command, target, sourceIndex); }
            catch (Exception ex) { Console.WriteLine($"Command failed ({command}): {ex.Message}"); }
        });
    }

    private void OnTouchButtonPress(object sender, TouchEventArgs e)
    {
        // Per-button override: native haptic skips these buttons entirely, so we
        // drive the legacy software Vibrate() pulse on both touch start and end.
        if (e.EventType == Constants.TouchEventType.TOUCH_END)
        {
            // A side-strip swipe-follow drag commits/snaps-back (or taps) on release;
            // non-animated strips (plugin-override / single-page segmented) resolve their
            // deferred tap-vs-swipe here so a swipe doesn't fire the strip command.
            HandleStripDragEnd(e.ChangedTouch);
            HandleStripTapEnd(e.ChangedTouch);

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

        if (exclusiveMode.IsActive)
        {
            foreach (var touch in e.Touches)
            {
                try { exclusiveMode.Current?.OnTouchPressed(touch.Target.Key); }
                catch (Exception ex) { Console.WriteLine($"Exclusive touch: {ex.Message}"); }
            }
            return;
        }

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

            // Side strips are label + swipe areas, not command buttons. A tap does nothing
            // in free-draw mode; in plugin-override mode it goes to the owning provider; in
            // segmented mode it goes to the per-segment session (e.g. tap an audio segment to
            // toggle mute — a non-audio segment's session call is a no-op). Either way it must
            // not arm the sliding-prevention slot, which guards the centre grid.
            if (IsSideStripSlot(slot))
            {
                var stripSide = slot == LoupedeckDevice.Device.RazerStreamControllerDevice.RightSideIndex
                    ? RotarySide.Right
                    : RotarySide.Left;

                // Segmented / free-draw strips run the finger-follow swipe animation: each
                // TOUCH_START packet (start + the device's mid-drag run) feeds the drag,
                // which renders the slide and routes taps on release.
                if (StripAnimationApplicable(stripSide))
                {
                    OnStripTouchSample(stripSide, SideIndex(stripSide), touch.Y, touch.Id);
                    continue;
                }

                // Non-animated strips (plugin-override, or segmented with a single rotary
                // page): track the gesture and defer the tap to release. HandleStripTapEnd
                // only routes it to the owning session if the finger barely moved, so a swipe
                // doesn't fire the strip command (e.g. an audio segment muting on a swipe).
                TrackStripTapSample(SideIndex(stripSide), touch.Y, touch.Id);
                continue;
            }

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
            FireAndForget(wrapped, ButtonTargets.TouchButton);
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
        if (exclusiveMode.IsActive)
        {
            if (TryGetRotaryIndex(e.ButtonId, out var rIdx))
            {
                try { exclusiveMode.Current?.OnRotated(rIdx, e.Delta); }
                catch (Exception ex) { Console.WriteLine($"Exclusive rotate: {ex.Message}"); }
            }
            return;
        }

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
        var resolved = ResolveRotaryButton(idx);
        if (resolved == null) return;
        var (page, btn) = resolved.Value;
        if (_isDeviceOff && !btn.EnableWhenOff) return;
        var leftTurn = e.Delta < 0;
        var command = leftTurn ? btn.RotaryLeftCommand : btn.RotaryRightCommand;
        if (string.IsNullOrEmpty(command)) return;
        var wrap = leftTurn ? page.KnobLeftWrap : page.KnobRightWrap;
        var wrapped = wrap?.Apply(command) ?? command;
        FireAndForget(wrapped, ButtonTargets.RotaryEncoder, idx);
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

    // Serializes exclusive-mode redraws and coalesces bursts. A chatty provider
    // (e.g. a Spotify progress bar) raises EntriesChanged many times a second;
    // without this, overlapping async-void redraw loops interleave their per-slot
    // FRAMEBUFF/DRAW pairs on the serial queue and a DRAW presents a half-written
    // buffer — visible as tearing on solid backgrounds.
    private readonly SemaphoreSlim _exclusiveRedrawGate = new(1, 1);
    private long _exclusiveGen;        // bumped on every change request
    private long _exclusiveDrawnGen;   // generation already rendered
    private long _lastExclusiveDrawTs; // Stopwatch timestamp of the last redraw

    // Minimum gap between full redraws (~30 fps). Keeps a continuously-updating
    // provider from driving the panel faster than it cleanly presents. The device
    // tops out around 35–44 fps for a full repaint, so this also leaves headroom.
    private const double ExclusiveMinRedrawMs = 1000.0 / 30.0;

    private static double StopwatchMs(long ticks) =>
        ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    private async void OnExclusiveStateChanged()
    {
        // A whole-device takeover owns the strips too: stop any plugin-strip providers
        // (idempotent). They re-attach when exclusive mode exits via RedrawSideStrips.
        if (exclusiveMode.IsActive)
            DetachAllSideStripProviders();

        var requested = Interlocked.Increment(ref _exclusiveGen);

        await _exclusiveRedrawGate.WaitAsync();
        try
        {
            // Coalesced away: an earlier waiter already rendered state at least
            // as fresh as this request. (BuildTouchEntries reads live provider
            // state, so the newest content is always what gets drawn.)
            if (Interlocked.Read(ref _exclusiveDrawnGen) >= requested)
                return;

            // Rate-limit: keep a minimum gap between full redraws.
            var lastTs = Interlocked.Read(ref _lastExclusiveDrawTs);
            if (lastTs != 0)
            {
                var wait = ExclusiveMinRedrawMs - StopwatchMs(System.Diagnostics.Stopwatch.GetTimestamp() - lastTs);
                if (wait > 0)
                    await Task.Delay((int)Math.Ceiling(wait));
            }

            var snapshot = Interlocked.Read(ref _exclusiveGen);
            await RedrawExclusiveOnce();
            Interlocked.Exchange(ref _exclusiveDrawnGen, snapshot);
            Interlocked.Exchange(ref _lastExclusiveDrawTs, System.Diagnostics.Stopwatch.GetTimestamp());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exclusive redraw failed: {ex.Message}");
        }
        finally
        {
            _exclusiveRedrawGate.Release();
        }
    }

    private async Task RedrawExclusiveOnce()
    {
        var device = deviceService.Device;
        if (device == null) return;

        if (exclusiveMode.IsActive)
        {
            var provider = exclusiveMode.Current;

            // Map provider's entries by slot for quick lookup; remaining
            // slots get blanked so leftover page content can't bleed
            // through. Slot bounds match the folder renderer.
            var entries = provider?.BuildTouchEntries() ?? Array.Empty<PluginSdk.FolderEntry>();
            var bySlot = new Dictionary<int, PluginSdk.FolderEntry>(entries.Count);
            foreach (var e in entries)
            {
                if (e != null) bySlot[e.SlotIndex] = e;
            }

            // The provider chooses how its frames reach the device (see
            // ExclusiveRenderMode). SDK plugins default to FullScreen; per-tile
            // modes trade the single big blit for many small framebuffer writes
            // (no DRAW), which is the lever for higher frame rates on live data.
            var mode = provider?.RenderMode ?? PluginSdk.ExclusiveRenderMode.FullScreen;

            switch (mode)
            {
                case PluginSdk.ExclusiveRenderMode.SingleTile:
                    ResetDirtyTiles();
                    await DrawExclusiveSingleTile(device, bySlot, provider.SingleTileSlot);
                    return;

                case PluginSdk.ExclusiveRenderMode.Grid:
                    ResetDirtyTiles();
                    await DrawExclusiveGrid(device, bySlot);
                    return;

                case PluginSdk.ExclusiveRenderMode.DirtyTiles:
                    await DrawExclusiveDirtyTiles(device, provider, bySlot);
                    return;

                default: // FullScreen
                    ResetDirtyTiles();
                    await DrawExclusiveFullScreen(device, bySlot);
                    return;
            }
        }

        // Exclusive ended — repaint the active page.
        ResetDirtyTiles();
        if (config.CurrentTouchButtonPage?.TouchButtons != null)
        {
            foreach (var touchButton in config.CurrentTouchButtonPage.TouchButtons)
            {
                await device.DrawTouchButton(touchButton, config, true, device.Columns);
            }
        }

        await RedrawSideStrips();
    }

    // FullScreen: render every slot, push the whole frame in ONE atomic blit + DRAW.
    // Drawing slot-by-slot here would refresh the full display 15× per frame.
    private async Task DrawExclusiveFullScreen(LoupedeckDevice.Device.LoupedeckDevice device,
        IReadOnlyDictionary<int, PluginSdk.FolderEntry> bySlot)
    {
        var slotBitmaps = new SkiaSharp.SKBitmap[FolderConstants.TotalSlots];
        for (var slot = 0; slot < FolderConstants.TotalSlots; slot++)
            slotBitmaps[slot] = RenderSlot(bySlot, slot);

        await device.DrawTouchSlotsAtomic(slotBitmaps, refresh: true);

        foreach (var b in slotBitmaps) b?.Dispose();
    }

    // Grid: every slot as its own 90x90 framebuffer, no DRAW.
    private async Task DrawExclusiveGrid(LoupedeckDevice.Device.LoupedeckDevice device,
        IReadOnlyDictionary<int, PluginSdk.FolderEntry> bySlot)
    {
        for (var slot = 0; slot < FolderConstants.TotalSlots; slot++)
        {
            using var bmp = RenderSlot(bySlot, slot);
            await device.DrawTouchSlot(slot, bmp, refresh: false);
        }
    }

    // SingleTile: draw just one 90x90 slot, no DRAW.
    private async Task DrawExclusiveSingleTile(LoupedeckDevice.Device.LoupedeckDevice device,
        IReadOnlyDictionary<int, PluginSdk.FolderEntry> bySlot, int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= FolderConstants.TotalSlots) slotIndex = 0;
        using var bmp = RenderSlot(bySlot, slotIndex);
        await device.DrawTouchSlot(slotIndex, bmp, refresh: false);
    }

    // DirtyTiles: like Grid, but only re-send slots whose visible content changed
    // since the last frame. A new provider (or first frame) repaints everything.
    private async Task DrawExclusiveDirtyTiles(LoupedeckDevice.Device.LoupedeckDevice device,
        PluginSdk.IExclusiveModeProvider provider,
        IReadOnlyDictionary<int, PluginSdk.FolderEntry> bySlot)
    {
        if (!ReferenceEquals(_dirtyOwner, provider) || _dirtyKeys == null)
        {
            _dirtyOwner = provider;
            _dirtyKeys = new TileSig?[FolderConstants.TotalSlots]; // all null → redraw all
        }

        for (var slot = 0; slot < FolderConstants.TotalSlots; slot++)
        {
            var sig = bySlot.TryGetValue(slot, out var entry) ? TileSig.Of(entry) : TileSig.Empty;
            var prev = _dirtyKeys[slot];
            if (prev.HasValue && prev.Value.Equals(sig))
                continue; // unchanged — skip the serial write entirely

            using (var bmp = RenderSlot(bySlot, slot))
                await device.DrawTouchSlot(slot, bmp, refresh: false);

            _dirtyKeys[slot] = sig;
        }
    }

    private SkiaSharp.SKBitmap RenderSlot(IReadOnlyDictionary<int, PluginSdk.FolderEntry> bySlot, int slot)
        => bySlot.TryGetValue(slot, out var entry)
            ? RenderSdkEntry(entry, slot)
            : BitmapHelper.RenderEmptyFolderSlot(config, slot, 90, 90, FolderConstants.Columns);

    // --- DirtyTiles bookkeeping -------------------------------------------------
    private PluginSdk.IExclusiveModeProvider _dirtyOwner;
    private TileSig?[] _dirtyKeys;

    private void ResetDirtyTiles()
    {
        _dirtyOwner = null;
        _dirtyKeys = null;
    }

    /// <summary>Visible signature of a touch slot — two equal signatures render to
    /// the same pixels, so the DirtyTiles path can skip re-sending the tile.</summary>
    private readonly record struct TileSig(
        string Text, PluginSdk.PluginColor Back, PluginSdk.PluginColor Fore, int TextSize, bool Bold, int ImageHash)
    {
        public static readonly TileSig Empty = new("<empty>", default, default, 0, false, 0);

        public static TileSig Of(PluginSdk.FolderEntry e)
            => new(e.Text ?? string.Empty, e.BackColor, e.TextColor, e.TextSize, e.Bold, HashImage(e.Image));

        private static int HashImage(byte[] img)
        {
            if (img == null || img.Length == 0) return 0;
            unchecked
            {
                var h = (int)2166136261;
                foreach (var b in img) h = (h ^ b) * 16777619;
                return h;
            }
        }
    }

    /// <summary>Adapts an SDK FolderEntry to the core FolderEntry renderer.</summary>
    private static SkiaSharp.SKBitmap RenderSdkEntry(PluginSdk.FolderEntry e, int slot)
    {
        var core = new Services.FolderNavigation.FolderEntry
        {
            SlotIndex = e.SlotIndex,
            Text = e.Text,
            BackColor = Avalonia.Media.Color.FromArgb(e.BackColor.A, e.BackColor.R, e.BackColor.G, e.BackColor.B),
            TextColor = Avalonia.Media.Color.FromArgb(e.TextColor.A, e.TextColor.R, e.TextColor.G, e.TextColor.B),
            TextSize = e.TextSize,
            Bold = e.Bold
        };
        return BitmapHelper.RenderFolderEntry(core, null, slot, 90, 90, FolderConstants.Columns);
    }

    private async void OnFolderStateChanged()
    {
        try
        {
            var device = deviceService.Device;
            if (device == null) return;

            // Exclusive mode owns the display — skip folder repaints, they'd
            // race with the exclusive provider's slot updates.
            if (exclusiveMode.IsActive) return;

            if (folderNav.IsActive)
            {
                // Folder navigation paints the whole screen including the strips, so
                // stop any plugin-strip providers; they re-attach on folder exit.
                DetachAllSideStripProviders();

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

                await RedrawSideStrips();
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

        // The side strips share the page wallpaper, so repaint them for the new page.
        if (!_isDeviceOff && !folderNav.IsActive && !exclusiveMode.IsActive)
            _ = RedrawSideStrips();
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
                case nameof(TouchButtonPage.WallpaperInvalidated):
                    await Task.Delay(100, token); // Debounce
                    foreach (var touchButton in config.CurrentTouchButtonPage.TouchButtons)
                    {
                        await deviceService.Device.DrawTouchButton(touchButton, config, true, deviceService.Device.Columns);
                        await Task.Delay(0, token);
                    }
                    // The side strips share the wallpaper — repaint them too.
                    await RedrawSideStrips();
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

        // Folder mode or exclusive mode owns the touch display — suppress per-button
        // redraws (e.g. from DynamicTextManager) so they don't paint over the active
        // view. When the override exits, the corresponding handler repaints the page.
        if (folderNav.IsActive || exclusiveMode.IsActive) return;

        var button = config.CurrentTouchButtonPage.TouchButtons.FirstOrDefault(b => b.Index == item.Index);

        if (button == null) return;

        await deviceService.Device.DrawTouchButton(button, config, true, deviceService.Device.Columns);
    }

    /// <summary>
    /// Subscribes a page's free-draw <see cref="RotaryButtonPage.StripCanvas"/> to the
    /// live-redraw pipeline, so editing its layers repaints the side strip immediately
    /// (mirroring <see cref="TouchItemChanged"/> for grid buttons). Idempotent; safe to
    /// call each time the strip editor opens. No-op without side strips / no canvas.
    /// </summary>
    public void RegisterStripCanvas(RotaryButtonPage page)
    {
        if (deviceService.Device?.HasSideStrips != true || page?.StripCanvas == null) return;
        page.StripCanvas.ItemChanged -= StripCanvasItemChanged;
        page.StripCanvas.ItemChanged += StripCanvasItemChanged;
    }

    private async void StripCanvasItemChanged(object sender, EventArgs e)
    {
        if (sender is not TouchButton canvas) return;
        if (_isDeviceOff || folderNav.IsActive || exclusiveMode.IsActive) return;

        // The canvas Index encodes its column (LeftSideIndex / RightSideIndex), so the
        // strip is repainted via the free-draw renderer, not the grid touch path.
        var side = canvas.Index == LoupedeckDevice.Device.RazerStreamControllerDevice.RightSideIndex
            ? RotarySide.Right
            : RotarySide.Left;

        await DrawSideStrip(side);
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

    /// <summary>
    /// Re-renders the baked simple-button images for the current theme. Called when the
    /// app theme variant changes so the LED/RGB button plastic follows Light/Dark. The
    /// device LED colours are unaffected (they're device state, not chrome). Must run on
    /// the UI thread — it assigns RenderedImage, which the UI binds to.
    /// </summary>
    public void RefreshRenderedButtonChrome()
    {
        if (config.SimpleButtons == null) return;

        foreach (var button in config.SimpleButtons)
        {
            if (button == null) continue;
            button.RenderedImage = BitmapHelper.RenderSimpleButtonImage(button, 90, 90);
        }
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
            if (page == null) continue;

            // The page wallpapers' originals (main + optional side displays) live in
            // the asset folder too.
            if (!string.IsNullOrWhiteSpace(page.MainWallpaper?.AssetPath))
                yield return page.MainWallpaper.AssetPath;
            if (!string.IsNullOrWhiteSpace(page.LeftWallpaper?.AssetPath))
                yield return page.LeftWallpaper.AssetPath;
            if (!string.IsNullOrWhiteSpace(page.RightWallpaper?.AssetPath))
                yield return page.RightWallpaper.AssetPath;

            if (page.TouchButtons == null) continue;
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