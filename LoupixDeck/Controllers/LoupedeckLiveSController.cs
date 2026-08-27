using System.ComponentModel;
using LoupixDeck.LoupedeckDevice;
using LoupixDeck.Models;
using LoupixDeck.Models.Extensions;
using LoupixDeck.Models.Layers;
using LoupixDeck.PluginSdk;
using LoupixDeck.Registry;
using LoupixDeck.Services;
using LoupixDeck.Services.FolderNavigation;
using LoupixDeck.Services.Macros;
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
    IFullDisplayRenderService fullDisplay,
    ISideStripProviderRegistry sideStripRegistry,
    IAssetService assetService,
    INativeHapticService nativeHapticService,
    Services.Animation.IAnimationScheduler animationScheduler,
    Services.Screensaver.IScreensaverManager screensaver,
    LoupedeckConfig config,
    DeviceRegistry.DeviceInfo deviceInfo,
    ResolvedDevice resolved,
    IServiceProvider serviceProvider,
    IDeviceRouter router) : IDeviceController
{
    private readonly string _configPath = deviceInfo != null
        ? FileDialogHelper.GetConfigPath(deviceInfo, resolved?.Serial)
        : FileDialogHelper.GetConfigPath("config.json");

    public IPageManager PageManager => pageManager;

    public LoupedeckConfig Config => config;

    /// <summary>True for devices with the two 60×270 side displays (Razer / Loupedeck CT).
    /// Lets the UI tell a side-strip touch button apart from a normal grid button.</summary>
    public bool HasSideStrips => deviceService.Device?.HasSideStrips == true;

    private volatile bool _isDeviceOff;
    public bool IsDeviceOff => _isDeviceOff;

    /// <summary>
    /// True while the off state was entered by the host suspending, not by the user. The
    /// hardware power-cycles across a suspend, so the next connect is proof the device is
    /// back and must be switched on again — without waiting for a resume event the OS may
    /// never deliver (Modern Standby / S0, issue #195). A user-toggled off device keeps
    /// this false and stays blank across the same reconnect.
    /// </summary>
    private volatile bool _blankedForSuspend;

    // True while the full-display screensaver owns the display (issue #120). Like
    // _isDeviceOff, this gates the controller's own redraw paths so dynamic text /
    // side-strip provider frames don't paint over the video. NOT the plugin exclusive
    // mode (that stays reserved for plugin takeovers).
    private volatile bool _screensaverActive;

    // True while a plugin owns the whole display via the raw full-display renderer path (issue
    // #124, e.g. video streaming). Gates the controller's own redraw paths exactly like
    // _screensaverActive so dynamic text / side-strip frames don't paint over the plugin's frames.
    private volatile bool _fullDisplayActive;

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

    public Task ClearDeviceState() => EnterOffState(false);

    /// <summary>
    /// The host is suspending: blank the device exactly like the manual off, but remember
    /// that it was not the user's doing, so the next connect switches it back on (#195).
    /// A device the user had already turned off stays off across the suspend.
    /// </summary>
    public Task HandleSystemSuspend() => EnterOffState(true);

    private async Task EnterOffState(bool forSuspend)
    {
        if (_isDeviceOff) return;
        _isDeviceOff = true;
        _blankedForSuspend = forSuspend;
        // The device goes dark (or the machine suspends) — a held button's release will never
        // arrive, so nothing may keep waiting on it (#185).
        ReleaseAllPresses();
        // Disarm the screensaver while the device is manually off (don't run a video
        // against a blanked, brightness-0 display). RestoreDeviceState re-arms it.
        try { screensaver.Stop(); } catch { /* best effort */ }
        // A plugin full-display takeover (issue #124) is only paused, not released: the device
        // going off is a temporary state and the plugin keeps its producer warm, so the stream
        // resumes on RestoreDeviceState without the plugin having to re-enter.
        try { fullDisplay.SetPaused(true); } catch { /* best effort */ }
        // Stop any plugin-strip providers so their timers don't churn while off.
        DetachAllSideStripProviders();
        await PushBlankState();
    }

    /// <summary>
    /// Blanks the hardware: brightness to 0 and every LED button black. Split out of
    /// <see cref="ClearDeviceState"/> so a reconnect can re-apply it without touching the
    /// off-state bookkeeping (issue #195).
    /// </summary>
    /// <returns>True when everything reached the hardware. A dead handle swallows writes
    /// or throws halfway, and the caller must not treat that as a painted device.</returns>
    private async Task<bool> PushBlankState()
    {
        try
        {
            var device = deviceService.Device;
            if (device == null) return false;
            await device.SetBrightness(0);
            if (config.SimpleButtons != null)
            {
                foreach (var btn in config.SimpleButtons)
                {
                    if (btn == null) continue;
                    await device.SetButtonColor(btn.Id, Avalonia.Media.Colors.Black);
                }
            }
            // Firmware-side native haptic (0x2e) is no longer used — haptic runs through
            // the software Vibrate() pulse, which only fires on an actual touch, so a dark
            // display never buzzes. The 0x2e disable is intentionally not sent: it wedges
            // the firmware haptic engine / can freeze the display (see docs/NATIVE_HAPTIC.md).
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Blanking the device failed: {ex.Message}");
            return false;
        }
    }

    public async Task RestoreDeviceState()
    {
        if (!_isDeviceOff) return;
        _isDeviceOff = false;
        _blankedForSuspend = false;
        // Anything still tracked from before the device went off is stale by definition (#185).
        ReleaseAllPresses();
        await PushFullState();
    }

    /// <summary>
    /// Pushes the complete visible state — brightness, LED colours, the current touch page,
    /// the side strips and the firmware haptic — to the hardware. Split out of
    /// <see cref="RestoreDeviceState"/> so a reconnect can re-push it without the off-state
    /// guard (issue #195).
    /// </summary>
    /// <returns>True when the whole picture reached the hardware. A push that dies partway
    /// (brightness through, touch buttons not) leaves exactly the wrong picture #195 reports,
    /// so the caller has to be able to tell.</returns>
    private async Task<bool> PushFullState()
    {
        try
        {
            var device = deviceService.Device;
            if (device == null) return false;
            await device.SetBrightness(config.Brightness / 100.0);
            if (config.SimpleButtons != null)
            {
                foreach (var btn in config.SimpleButtons)
                {
                    if (btn == null) continue;
                    await device.SetButtonColor(btn.Id, btn.ButtonColor);
                }
            }
            // A paused full-display takeover (issue #124) still owns the display: resume its frames
            // instead of repainting the page over them. The screensaver stays disarmed while a
            // plugin owns the display, so there is nothing to re-arm here either.
            if (_fullDisplayActive)
            {
                try { fullDisplay.SetPaused(false); } catch { /* best effort */ }
                nativeHapticService.Apply();
                return true;
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
            // Device is back on — resume screensaver idle monitoring.
            try { screensaver.Arm(); } catch { /* best effort */ }
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Pushing the device state failed: {ex.Message}");
            return false;
        }
    }

    public Task ToggleDeviceState() => _isDeviceOff ? RestoreDeviceState() : ClearDeviceState();

    // ───────── Reconnect / system resume (issue #195) ─────────

    /// <summary>Serializes the state re-push so an auto-reconnect, the Settings "Reconnect"
    /// button and a system resume can't paint over each other.</summary>
    private readonly SemaphoreSlim _resyncGate = new(1, 1);

    /// <summary>Number of re-pushes running or waiting. One may queue behind the running
    /// one — a request that arrives mid-push must not be dropped, because it may be the
    /// one carrying the newer state (#195) — further requests would only repaint the same
    /// picture a third time and are skipped.</summary>
    private int _resyncWaiters;

    /// <summary>Time given to a freshly established link before the first frame goes out.
    /// The connect event fires before the read thread is up, so an immediate draw would
    /// wait on a response nobody reads yet.</summary>
    private const int ReconnectSettleMs = 1500;

    /// <summary>How long a resume keeps trying to rebuild the link. Three fixed tries were
    /// enough for an S3 wake, where the port is usually back before we even ask. A resume
    /// from hibernate or hybrid sleep re-enumerates USB during a firmware POST while the
    /// kernel image is still coming off disk, so the port can stay away far longer (#195).</summary>
    private static readonly TimeSpan ResumeReconnectWindow = TimeSpan.FromSeconds(30);

    public async Task ResyncDeviceState()
    {
        // One push running plus one queued is enough to cover every ordering; anything
        // beyond that would repaint an identical picture.
        if (Interlocked.Increment(ref _resyncWaiters) > 2)
        {
            Interlocked.Decrement(ref _resyncWaiters);
            return;
        }

        try
        {
            await _resyncGate.WaitAsync();
            try
            {
                // The device power-cycled while the host slept, so it is physically back
                // on — leaving it in the suspend-blanked state would repaint black and
                // require the manual off/on toggle (#195). A user-off device stays blank.
                var takeOnline = _blankedForSuspend;

                var pushed = (takeOnline || !_isDeviceOff)
                    ? await PushFullState()
                    : await PushBlankState();

                // A push into a dead handle fails silently or stops halfway. Clearing the
                // suspend mark anyway would leave the controller convinced the device is on
                // and painted, and nothing would ever repaint it — that is what still forced
                // the manual off/on toggle after a wake. So the mark survives a failed push
                // and the next connect (auto-reconnect or resume retry) tries again.
                if (!pushed) return;

                if (takeOnline)
                {
                    _blankedForSuspend = false;
                    _isDeviceOff = false;
                    ReleaseAllPresses();
                }
            }
            finally
            {
                _resyncGate.Release();
            }
        }
        finally
        {
            Interlocked.Decrement(ref _resyncWaiters);
        }
    }

    /// <summary>
    /// The serial link came back — after the device's own auto-reconnect, the Settings
    /// "Reconnect" button, or a system resume. The hardware kept nothing across the
    /// power cycle (the display still shows its boot image and the LEDs their boot
    /// colours), so the full state has to go out again; restoring only the transport is
    /// what left the device blank in issue #195.
    /// </summary>
    private void OnDeviceConnected(object sender, ConnectionEventArgs e)
    {
        // Raised from inside the connect call, i.e. before the read thread runs — hop off
        // that thread and let the link settle before drawing.
        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            await Task.Delay(ReconnectSettleMs);
            await ResyncDeviceState();
        });
    }

    public async Task HandleSystemResume()
    {
        // Give the USB stack time to re-enumerate the device after wake.
        await Task.Delay(1000);

        var device = deviceService.Device;
        if (device == null)
            return;

        // The handle the device woke up with is dead even when the OS still lists the port
        // and the handle still reports itself as open — writes then go nowhere. So the link
        // is torn down and rebuilt unconditionally (its connect event does the re-push),
        // never skipped just because the stale handle still looks healthy. The port can
        // need a few seconds to come back, so we keep trying for the whole window rather
        // than giving up after a fixed number of tries.
        var deadline = Environment.TickCount64 + (long)ResumeReconnectWindow.TotalMilliseconds;
        var retryDelayMs = 1000;

        while (true)
        {
            try
            {
                // Blocking: port tear-down, DTR probe and handshake add up to ~2 s, and
                // this runs on the UI thread (power event → dispatcher).
                await Task.Run(deviceService.ReconnectDevice);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Resume] reconnect failed: {ex.Message}");
            }

            if (device.IsConnected || Environment.TickCount64 >= deadline) break;

            await Task.Delay(retryDelayMs);
            retryDelayMs = Math.Min(retryDelayMs * 2, 4000);
        }

        // Always re-push at the end instead of trusting the connect event to have done it:
        // the resume may land before or after the reconnect, and a second push of the same
        // picture is harmless (redundant pushes coalesce in ResyncDeviceState).
        await Task.Delay(ReconnectSettleMs);
        await ResyncDeviceState();
    }

    public void Shutdown()
    {
        // Let go of every tracked press first — after this the release events stop arriving (#185).
        ReleaseAllPresses();

        // Persist any last runtime state before we close.
        try { SaveConfig(); } catch (Exception ex) { Console.WriteLine($"Shutdown SaveConfig failed: {ex.Message}"); }

        // Stop plugin strips so their timers don't keep churning against a dead device.
        try { DetachAllSideStripProviders(); } catch { /* best effort */ }

        // Stop the screensaver (and its idle timer) before halting the animation loop.
        try
        {
            screensaver.Started -= OnScreensaverStarted;
            screensaver.Stopped -= OnScreensaverStopped;
            fullDisplay.Started -= OnFullDisplayStarted;
            fullDisplay.Stopped -= OnFullDisplayStopped;
            screensaver.Stop();
        }
        catch { /* best effort */ }

        // Release any plugin full-display takeover (issue #124) so the plugin's OnStop runs and its
        // source leaves the scheduler before the animation loop is halted.
        try { fullDisplay.StopActive(); } catch { /* best effort */ }

        // Detach the transition sources before halting the loop.
        try { UnregisterStripAnimationSource(); } catch { /* best effort */ }
        try { UnregisterTouchAnimationSource(); } catch { /* best effort */ }

        // Halt the central animation loop so no frame is pushed to the gone device.
        try { animationScheduler.Stop(); } catch { /* best effort */ }

        var device = deviceService.Device;
        if (device != null)
        {
            device.OnButton -= OnSimpleButtonPress;
            device.OnTouch -= OnTouchButtonPress;
            device.OnRotate -= OnRotate;
            device.OnSwipe -= OnSwipe;
            device.OnConnect -= OnDeviceConnected;
            // Close() suppresses the device's auto-reconnect loop and releases the port.
            try { device.Close(); } catch (Exception ex) { Console.WriteLine($"Shutdown device close failed: {ex.Message}"); }
        }

        // Unsubscribe app-level events so nothing tries to repaint the gone device.
        pageManager.OnTouchPageChanged -= OnTouchPageChanged;
        pageManager.OnRotaryPageChanged -= OnRotaryPageChanged;
        folderNav.StateChanged -= OnFolderStateChanged;
        exclusiveMode.StateChanged -= OnExclusiveStateChanged;
        config.PropertyChanged -= ConfigOnPropertyChanged;

        // Detach from whichever workspace's pages we are currently bound to (issue #132).
        if (_boundTouchPages != null)
        {
            _boundTouchPages.CollectionChanged -= TouchButtonPagesOnCollectionChanged;
            foreach (var page in _boundTouchPages)
                page.PropertyChanged -= TouchButtonPageOnPropertyChanged;
            _boundTouchPages = null;
        }
    }

    public async Task RedrawCurrentTouchPage()
    {
        // No-op while something else owns the screen — the owner repaints when it
        // releases (device-off → RestoreDeviceState, folder/exclusive → their exit
        // handlers), so painting here would fight them.
        if (_isDeviceOff || folderNav.IsActive || _screensaverActive || _fullDisplayActive)
            return;

        var device = deviceService.Device;
        if (device == null || config.CurrentTouchButtonPage?.TouchButtons == null)
            return;

        // An exclusive provider suppresses only the surfaces it claimed (#127); the rest
        // of the page keeps rendering normally.
        if (!exclusiveMode.Owns(ExclusiveControlScope.TouchButtons))
        {
            var stripsTaken = exclusiveMode.Owns(ExclusiveControlScope.SideDisplays);
            foreach (var tb in config.CurrentTouchButtonPage.TouchButtons)
            {
                // Slots 12/13 are the side strips; skip them only when the provider owns
                // the side displays, so a grid repaint can't paint over its strips.
                if (stripsTaken && IsSideStripSlot(tb.Index)) continue;
                await device.DrawTouchButton(tb, config, true, device.Columns);
            }
        }

        await RedrawSideStrips();
    }

    // ───────── Screensaver (issue #120) ─────────

    /// <summary>The screensaver took over the display: suppress our own redraws and stop the
    /// side-strip provider timers so their frames can't interleave with the video.</summary>
    private void OnScreensaverStarted()
    {
        _screensaverActive = true;
        try { DetachAllSideStripProviders(); } catch { /* best effort */ }
    }

    /// <summary>The screensaver released the display: repaint the active page (this also
    /// re-attaches the side-strip providers).</summary>
    private void OnScreensaverStopped()
    {
        _screensaverActive = false;
        _ = RedrawCurrentTouchPage();
    }

    // ───────── Plugin full-display renderer (issue #124) ─────────

    /// <summary>A plugin took the whole display over via the raw full-display renderer: suppress our
    /// own redraws and stop the side-strip provider timers, exactly as for the screensaver.</summary>
    private void OnFullDisplayStarted()
    {
        _fullDisplayActive = true;
        try { DetachAllSideStripProviders(); } catch { /* best effort */ }
    }

    /// <summary>The plugin released the display: repaint the active page (this also re-attaches the
    /// side-strip providers).</summary>
    private void OnFullDisplayStopped()
    {
        _fullDisplayActive = false;
        _ = RedrawCurrentTouchPage();
    }

    /// <summary>
    /// While a plugin full-display takeover (issue #124) is active the device is fully owned: buttons
    /// have no normal function and any press ends the stream (mirroring the screensaver wake gesture).
    /// Returns true when the input was consumed for that purpose, so the caller must not run the normal
    /// action. Stops off the input thread so decoder teardown never stalls input handling.
    /// </summary>
    private bool StopFullDisplayOnInput()
    {
        if (!_fullDisplayActive)
            return false;

        _ = Task.Run(fullDisplay.StopActive);
        return true;
    }

    /// <summary>Ends any active full-display or exclusive-mode takeover. Called on a profile/workspace
    /// switch — the takeover belonged to the workspace being left, and neither mode auto-restarts
    /// (the owning plugin re-enters via its own command). Safe to call when nothing is active.</summary>
    private void StopDisplayTakeovers()
    {
        try
        {
            if (exclusiveMode.IsActive)
                exclusiveMode.Exit(exclusiveMode.Current);
        }
        catch (Exception ex) { Console.WriteLine($"StopDisplayTakeovers (exclusive) failed: {ex.Message}"); }

        try { fullDisplay.StopActive(); }
        catch (Exception ex) { Console.WriteLine($"StopDisplayTakeovers (full-display) failed: {ex.Message}"); }
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

        // Bind the active workspace's touch pages (per-page wallpaper property changes + the
        // collection itself). Factored so a workspace/profile switch can rebind onto the new
        // workspace's pages (issue #132).
        BindActiveWorkspaceTouchPages();

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

            // ApplyTouchPage early-returns here (the index was pre-set), so OnTouchPageChanged
            // does not fire — wire the current page's ItemChanged explicitly (tracked so a later
            // page/workspace switch detaches it cleanly).
            AttachTouchItemChanged(config.CurrentTouchButtonPage);

            foreach (var touchButton in config.CurrentTouchButtonPage.TouchButtons)
            {
                await deviceService.Device.DrawTouchButton(touchButton, config, true, deviceService.Device.Columns);
            }
        }

        // Rotary selection is already set by InitializeRotaryPages (per side on
        // side-strip devices, where CurrentRotaryButtonPage/Both is intentionally null).
        config.CurrentRotaryButtonPage?.Selected = true;
        config.CurrentTouchButtonPage.Selected = true;

        config.PropertyChanged += ConfigOnPropertyChanged;

        deviceService.Device.DitherFramebuffer = config.DitheringEnabled;
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

        // Begin idle monitoring for the screensaver now that the device is fully up
        // and the startup page is drawn (issue #120). Any input resets the countdown.
        screensaver.Started += OnScreensaverStarted;
        screensaver.Stopped += OnScreensaverStopped;
        fullDisplay.Started += OnFullDisplayStarted;
        fullDisplay.Stopped += OnFullDisplayStopped;
        screensaver.Arm();

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
        // Wired only here, after Initialize has already drawn everything, so the very
        // first connect doesn't repaint a second time — every later connect does (#195).
        device.OnConnect += OnDeviceConnected;

        // Repaint the affected side strip whenever its rotary page changes.
        pageManager.OnRotaryPageChanged += OnRotaryPageChanged;

        // Register the side-strip transition source on the central animation scheduler so
        // swipe-release and command/GUI-driven rotary page slides are paced centrally (#119).
        RegisterStripAnimationSource();

        // Register the touch-page transition source on the same central scheduler.
        RegisterTouchAnimationSource();
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
        // Mark this device as the ambient target so any plugin (side-strip session,
        // exclusive provider) reached while handling this input acts on THIS device.
        using var _routerScope = router.Enter(serviceProvider);

        // Any hardware input resets the screensaver idle timer; when it stops a running
        // screensaver, that input was a "wake" gesture — consume it (no normal action).
        if (screensaver.NotifyActivity()) return;

        if (StopFullDisplayOnInput()) return;

        if (_isDeviceOff || exclusiveMode.Owns(ExclusiveControlScope.SideDisplays) ||
            folderNav.IsActive || _screensaverActive || _fullDisplayActive)
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

        // Device swipe arrives on a background thread; marshal the bound-state page change
        // onto the UI thread, mirroring the GUI paging path.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (e.Direction == SwipeDirection.Up)
                pageManager.NextRotaryPage(side);
            else
                pageManager.PreviousRotaryPage(side);
        });
    }

    private async void OnRotaryPageChanged(RotarySide side, int previousIndex, int newIndex)
    {
        // Only side-strip devices render rotary labels onto a strip.
        if (deviceService.Device?.HasSideStrips != true || side == RotarySide.Both)
            return;

        if (_isDeviceOff || exclusiveMode.Owns(ExclusiveControlScope.SideDisplays) ||
            folderNav.IsActive || _screensaverActive || _fullDisplayActive)
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
        slotButton?.RenderedImage = strip;

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
    /// them (no-op on devices without side strips, or while an exclusive provider owns
    /// the side displays).</summary>
    private async Task RedrawSideStrips()
    {
        if (deviceService.Device?.HasSideStrips != true) return;
        if (exclusiveMode.Owns(ExclusiveControlScope.SideDisplays)) return;
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
        if (_isDeviceOff || folderNav.IsActive || exclusiveMode.Owns(ExclusiveControlScope.SideDisplays) ||
            _screensaverActive || _fullDisplayActive) return;
        await RedrawSideStrips();
    }

    /// <inheritdoc/>
    public Task RefreshSideStripAnimationFrame(RotarySide side)
    {
        if (deviceService.Device?.HasSideStrips != true || side == RotarySide.Both)
            return Task.CompletedTask;

        var idx = SideIndex(side);

        // A swipe drag/settle owns the strip until it lands; a frame push mid-swipe would
        // draw the page flat at offset 0 and fight the slide. Skip this frame; the next tick
        // (or the transition's own commit) repaints. Route through the shared coalescing gate
        // so an animation frame can't double-push against a provider redraw or the swipe.
        if (IsStripDragBusy(idx)) return Task.CompletedTask;

        return RedrawStripCoalesced(side, idx);
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
        {
            return;
        }

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
            if (_isDeviceOff || folderNav.IsActive || exclusiveMode.Owns(ExclusiveControlScope.SideDisplays) ||
                _screensaverActive || _fullDisplayActive) return;

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
        // A release ends the trigger-press token (#185). Done first and deliberately without
        // returning, so a screensaver wake or a display takeover consuming this event cannot
        // strand a macro waiting for the button to come up. A release still falls out at the
        // BUTTON_DOWN filter below, exactly as before.
        if (e.EventType != Constants.ButtonEventType.BUTTON_DOWN)
            CompleteButtonPress(e.ButtonId);

        using var _routerScope = router.Enter(serviceProvider);

        // Any hardware input resets the screensaver idle timer; when it stops a running
        // screensaver, that input was a "wake" gesture — consume it (no normal action).
        if (screensaver.NotifyActivity()) return;

        if (e.EventType != Constants.ButtonEventType.BUTTON_DOWN)
            return;

        if (StopFullDisplayOnInput()) return;

        if (exclusiveMode.IsActive)
        {
            // Exclusive provider receives the raw 0-based button index. Rotary
            // presses are forwarded through OnRotaryPressed; everything else is
            // a simple button. Only the control categories the provider actually
            // declared are taken over (#127) — the rest falls through below and
            // keeps running the user's normal assignment.
            if (TryGetRotaryIndex(e.ButtonId, out var rIdx))
            {
                if (exclusiveMode.Owns(ExclusiveControlScope.RotaryPress))
                {
                    try { exclusiveMode.Current?.OnRotaryPressed(rIdx); }
                    catch (Exception ex) { Console.WriteLine($"Exclusive rotary press: {ex.Message}"); }
                    return;
                }
            }
            else if (exclusiveMode.Owns(ExclusiveControlScope.SimpleButtons))
            {
                var sbIdx = Array.FindIndex(config.SimpleButtons ?? Array.Empty<SimpleButton>(),
                    b => b != null && b.Id == e.ButtonId);
                if (sbIdx >= 0)
                {
                    try { exclusiveMode.Current?.OnSimpleButtonPressed(sbIdx); }
                    catch (Exception ex) { Console.WriteLine($"Exclusive button press: {ex.Message}"); }
                }

                // Owned category: consume the press even when no configured button
                // matches, so an unmapped key can't leak into the normal path.
                return;
            }
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
            DispatchWithPress(e.ButtonId, () => FireSimpleButtonCommand(button, wrapped));
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
        DispatchWithPress(e.ButtonId, () => FireAndForget(wrappedRotary, ButtonTargets.RotaryEncoder, idx));
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

    /// <summary>
    /// Runs a simple (LED) button's active-state command off the serial-read thread. For a Local
    /// stateful button the state's transition is applied up front — resolved against the state that
    /// was active at press time — so the LED updates to the new state immediately instead of waiting
    /// for the command to finish (#186). External buttons never transition automatically; their
    /// state is driven by a plugin.
    /// </summary>
    private void FireSimpleButtonCommand(SimpleButton button, string wrapped)
    {
        if (button.Mode == ButtonStateMode.Local)
            ApplyStateTransition(button, button.ActiveState);

        _ = Task.Run(async () =>
        {
            try
            {
                if (!string.IsNullOrEmpty(wrapped))
                    await commandService.ExecuteCommand(wrapped, ButtonTargets.SimpleButton, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Command failed ({wrapped}): {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Runs a touch button's active-state command (wrapped by the page) off the serial-read
    /// thread. For a Local stateful button the state's transition is applied up front — resolved
    /// against the state that was active at press time — so the device repaints the new state
    /// immediately instead of waiting for the command to finish (#186). External buttons never
    /// transition automatically; their state is driven by a plugin.
    /// </summary>
    private void FireTouchButtonCommand(TouchButton button)
    {
        var wrapped = config.CurrentTouchButtonPage.TouchButtonWrap?.Apply(button.Command) ?? button.Command;

        if (button.Mode == ButtonStateMode.Local)
            ApplyStateTransition(button, button.ActiveState);

        _ = Task.Run(async () =>
        {
            try
            {
                if (!string.IsNullOrEmpty(wrapped))
                    await commandService.ExecuteCommand(wrapped, ButtonTargets.TouchButton, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Command failed ({wrapped}): {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Resolves <paramref name="current"/>'s transition to the next state id and applies it on the
    /// UI thread (never the serial-read thread). A Stay transition or a removed target is a no-op.
    /// Shared by touch and simple (LED) stateful buttons.
    /// <para>
    /// The state is passed in rather than re-read from the button: the caller captures it at press
    /// time, so a slow command can't make the transition resolve against a state the user has since
    /// moved on from (#186).
    /// </para>
    /// </summary>
    private static void ApplyStateTransition(StatefulButton button, ButtonState current)
    {
        var states = button.States;
        if (states == null || states.Count == 0) return;

        if (current == null) return;

        var transition = current.Transition ?? new StateTransition();
        Guid? nextId = transition.Kind switch
        {
            StateTransitionKind.Next => NeighbourStateId(states, current, +1),
            StateTransitionKind.Previous => NeighbourStateId(states, current, -1),
            StateTransitionKind.Specific => transition.TargetStateId,
            StateTransitionKind.ResetToDefault => button.DefaultState?.Id,
            _ => null // Stay
        };

        if (nextId is not { } target || target == current.Id) return;
        if (states.All(s => s.Id != target)) return; // target state was deleted

        Avalonia.Threading.Dispatcher.UIThread.Post(() => button.SetActiveState(target));
    }

    private static Guid? NeighbourStateId(IList<ButtonState> states, ButtonState current, int direction)
    {
        var index = -1;
        for (var i = 0; i < states.Count; i++)
        {
            if (states[i].Id == current.Id) { index = i; break; }
        }
        if (index < 0) return null;

        var next = ((index + direction) % states.Count + states.Count) % states.Count;
        return states[next].Id;
    }

    /// <summary>
    /// Resolves the haptic effect a touch on <paramref name="button"/> should play:
    /// the per-button override pattern when the button's own "Vibration enabled" is set,
    /// otherwise the global pattern's first effect (Settings -> Feedback) when global
    /// haptic is enabled. Returns null when neither applies, i.e. no vibration.
    /// </summary>
    private byte? ResolveVibrationPattern(TouchButton button)
    {
        if (button.VibrationEnabled)
            return button.VibrationPattern;

        if (config.HapticEnabled && config.HapticSteps.Count > 0)
            return config.HapticSteps[0].Effect;

        return null;
    }

    private void OnTouchButtonPress(object sender, TouchEventArgs e)
    {
        // A lifted finger ends its trigger-press token (#185). Done before every early return
        // below so a wake gesture, a takeover or a failing strip handler cannot strand a macro
        // waiting for the touch to end. The device removes the id from its touch set before
        // raising this, so ChangedTouch is the contact that was lifted.
        if (e.EventType == Constants.TouchEventType.TOUCH_END && e.ChangedTouch != null)
            CompleteTouchPress(e.ChangedTouch.Id);

        using var _routerScope = router.Enter(serviceProvider);

        // Any hardware input resets the screensaver idle timer; when it stops a running
        // screensaver, that input was a "wake" gesture — consume it (no normal action).
        if (screensaver.NotifyActivity()) return;

        // Haptic feedback is driven by the software Vibrate() pulse (per-button override
        // or the global pattern). Silence any active pulse on release the same way.
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
                if (btn != null && ResolveVibrationPattern(btn).HasValue)
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

        if (StopFullDisplayOnInput()) return;

        // Exclusive mode freezes folder navigation, so the folder path only applies when
        // no provider is active at all. Slots the provider does not claim (#127) fall
        // through to the normal per-slot handling below.
        // On a device whose grid is physical keys, a held key stays in the touch set for
        // as long as it is down, and every further press re-raises TOUCH_START with the
        // whole set. Acting on all of them would re-fire a held key's command on each new
        // press, so only the contact that actually changed is handled. A real touchscreen
        // never re-sends TOUCH_START for a resting finger, so it keeps the old behaviour.
        var physicalKeys = deviceService.Device?.GridIsPhysicalKeys == true;

        if (!exclusiveMode.IsActive && folderNav.IsActive)
        {
            foreach (var touch in e.Touches)
            {
                if (physicalKeys && e.ChangedTouch != null && touch.Id != e.ChangedTouch.Id)
                    continue;

                HandleFolderTouch(touch.Target.Key);
            }
            return;
        }

        foreach (var touch in e.Touches)
        {
            if (physicalKeys && e.ChangedTouch != null && touch.Id != e.ChangedTouch.Id)
                continue;

            var slot = touch.Target.Key;

            // A slot belongs to the exclusive provider only when its scope covers that
            // slot's category: the two side-strip slots need SideDisplays, every grid
            // slot needs TouchButtons.
            if (exclusiveMode.Owns(IsSideStripSlot(slot)
                    ? ExclusiveControlScope.SideDisplays
                    : ExclusiveControlScope.TouchButtons))
            {
                try { exclusiveMode.Current?.OnTouchPressed(slot); }
                catch (Exception ex) { Console.WriteLine($"Exclusive touch: {ex.Message}"); }
                continue;
            }

            // Side strips are label + swipe areas, not command buttons. A tap does nothing
            // in free-draw mode; in plugin-override mode it goes to the owning provider; in
            // segmented mode it goes to the per-segment session (e.g. tap an audio segment to
            // toggle mute — a non-audio segment's session call is a no-op). Either way it must
            // not arm the sliding-prevention slot, which guards the centre grid.
            if (IsSideStripSlot(slot))
            {
                // The finger-follow tracks the live finger only. The device's touch set can
                // retain a stale contact whose TOUCH_END was lost to a framing resync; such
                // an entry is never the changed touch, so skipping non-changed touches here
                // keeps a leaked id from hijacking the drag (frozen strip / dead swipes).
                if (e.ChangedTouch != null && touch.Id != e.ChangedTouch.Id)
                    continue;

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

            // Per-button override wins; otherwise the global pattern (Settings -> Feedback)
            // applies to every button. Fires immediately on touch — no press-and-hold.
            byte? vibrationPattern = ResolveVibrationPattern(button);
            if (vibrationPattern.HasValue)
                deviceService.Device.Vibrate(vibrationPattern.Value);

            if (config.TouchFeedbackEnabled)
                _ = ShowTouchFeedback(button);

            // Track the press by touch id so a macro can wait for this finger to lift (#185).
            TriggerPress press = BeginTouchPress(touch.Id);
            using (TriggerPressScope.Enter(press))
                FireTouchButtonCommand(button);
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
        using var _routerScope = router.Enter(serviceProvider);

        // Any hardware input resets the screensaver idle timer; when it stops a running
        // screensaver, that input was a "wake" gesture — consume it (no normal action).
        if (screensaver.NotifyActivity()) return;

        if (StopFullDisplayOnInput()) return;

        // Only forwarded when the provider declared it overrides rotary turns (#127);
        // otherwise the dial keeps running the user's normal command below.
        if (exclusiveMode.Owns(ExclusiveControlScope.RotaryTurn))
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
            // Loupedeck CT's centre wheel is a 7th rotary, slotted after the 6 side dials.
            case Constants.ButtonType.KNOB_CT: index = 6; return true;
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
        // A takeover that claims the side displays owns the strips too: stop any
        // plugin-strip providers (idempotent). They re-attach when exclusive mode exits
        // via RedrawSideStrips. A provider that left the strips out (#127) keeps them
        // running — repaint them so a scope change takes effect right away.
        if (exclusiveMode.Owns(ExclusiveControlScope.SideDisplays))
            DetachAllSideStripProviders();
        else if (exclusiveMode.IsActive)
            await RedrawSideStrips();

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

            // A provider only paints the surfaces it declared (#127). Nothing claimed →
            // nothing to push here; the normal redraw paths are unblocked and own the page.
            var ownsGrid = exclusiveMode.Owns(ExclusiveControlScope.TouchButtons);
            var ownsStrips = exclusiveMode.Owns(ExclusiveControlScope.SideDisplays);
            if (!ownsGrid && !ownsStrips)
            {
                ResetDirtyTiles();
                return;
            }

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
                    await DrawExclusiveFullScreen(device, bySlot, ownsGrid, ownsStrips);
                    return;
            }
        }

        // Exclusive ended — repaint the active page (grid + strips).
        ResetDirtyTiles();
        await RedrawCurrentTouchPage();
    }

    /// <summary>True when the active exclusive provider claimed the surface this slot lives on:
    /// the two side-strip slots belong to SideDisplays, every other slot to TouchButtons.</summary>
    private bool ExclusiveOwnsSlot(int slot) =>
        exclusiveMode.Owns(IsSideStripSlot(slot)
            ? ExclusiveControlScope.SideDisplays
            : ExclusiveControlScope.TouchButtons);

    // FullScreen: render every slot, push the whole frame in ONE atomic blit + DRAW.
    // Drawing slot-by-slot here would refresh the full display 15× per frame.
    private async Task DrawExclusiveFullScreen(LoupedeckDevice.Device.LoupedeckDevice device,
        IReadOnlyDictionary<int, PluginSdk.FolderEntry> bySlot, bool ownsGrid, bool ownsStrips)
    {
        // The atomic push clears and rewrites the WHOLE centre buffer, which on a
        // side-strip device also covers the 60px strip regions. A provider that left the
        // strips out must not wipe them, so push just the grid region instead.
        if (ownsGrid && !ownsStrips && device.HasSideStrips)
        {
            await DrawExclusiveGridRegion(device, bySlot);
            return;
        }

        // Grid not claimed (strips only): no full-frame push exists for that, fall back
        // to the per-slot path so the normal page keeps the grid.
        if (!ownsGrid)
        {
            await DrawExclusiveGrid(device, bySlot);
            return;
        }

        var slotBitmaps = new SkiaSharp.SKBitmap[FolderConstants.TotalSlots];
        for (var slot = 0; slot < FolderConstants.TotalSlots; slot++)
            slotBitmaps[slot] = RenderSlot(bySlot, slot);

        await device.DrawTouchSlotsAtomic(slotBitmaps, refresh: true);

        foreach (var b in slotBitmaps) b?.Dispose();
    }

    // FullScreen for a grid-only takeover: composite the grid slots into one bitmap and
    // push it as the centre grid region, leaving the side-panel columns of the framebuffer
    // untouched. Same single blit + DRAW cost as the full-frame path.
    private async Task DrawExclusiveGridRegion(LoupedeckDevice.Device.LoupedeckDevice device,
        IReadOnlyDictionary<int, PluginSdk.FolderEntry> bySlot)
    {
        var keySize = device.KeySize;
        var gridSlots = device.Columns * device.Rows;

        using var grid = new SkiaSharp.SKBitmap(new SkiaSharp.SKImageInfo(
            device.Columns * keySize, device.Rows * keySize,
            SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul));

        // Composite under the shared render gate, mirroring DrawTouchSlotsAtomic.
        lock (SkiaRenderGate.Sync)
        {
            using var canvas = new SkiaSharp.SKCanvas(grid);
            canvas.Clear(SkiaSharp.SKColors.Black);
            for (var slot = 0; slot < gridSlots; slot++)
            {
                using var bmp = RenderSlot(bySlot, slot);
                if (bmp == null) continue;
                canvas.DrawBitmap(bmp, (slot % device.Columns) * keySize, (slot / device.Columns) * keySize);
            }
        }

        await device.DrawCenterGridRegion(grid, refresh: true);
    }

    // Grid: every slot as its own 90x90 framebuffer, no DRAW.
    private async Task DrawExclusiveGrid(LoupedeckDevice.Device.LoupedeckDevice device,
        IReadOnlyDictionary<int, PluginSdk.FolderEntry> bySlot)
    {
        for (var slot = 0; slot < FolderConstants.TotalSlots; slot++)
        {
            if (!ExclusiveOwnsSlot(slot)) continue;
            using var bmp = RenderSlot(bySlot, slot);
            await device.DrawTouchSlot(slot, bmp, refresh: false);
        }
    }

    // SingleTile: draw just one 90x90 slot, no DRAW.
    private async Task DrawExclusiveSingleTile(LoupedeckDevice.Device.LoupedeckDevice device,
        IReadOnlyDictionary<int, PluginSdk.FolderEntry> bySlot, int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= FolderConstants.TotalSlots) slotIndex = 0;
        if (!ExclusiveOwnsSlot(slotIndex)) return;
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
            if (!ExclusiveOwnsSlot(slot)) continue;

            var sig = bySlot.TryGetValue(slot, out var entry) ? TileSig.Of(entry) : TileSig.Empty;
            var prev = _dirtyKeys[slot];
            if (prev.HasValue && prev.Value.Equals(sig))
                continue; // unchanged — skip the serial write entirely

            using (var bmp = RenderSlot(bySlot, slot))
                await device.DrawTouchSlot(slot, bmp, refresh: false);

            _dirtyKeys[slot] = sig;
        }
    }

    /// <summary>Tile edge length of the attached device (90 on every Loupedeck, 96 on the
    /// Razer Stream Controller X). Bitmaps drawn to a touch slot must match it exactly.</summary>
    private int DeviceKeySize => deviceService.Device?.KeySize ?? 90;

    private SkiaSharp.SKBitmap RenderSlot(IReadOnlyDictionary<int, PluginSdk.FolderEntry> bySlot, int slot)
        => bySlot.TryGetValue(slot, out var entry)
            ? RenderSdkEntry(entry, slot, DeviceKeySize)
            : BitmapHelper.RenderEmptyFolderSlot(config, slot, DeviceKeySize, DeviceKeySize, FolderConstants.Columns);

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
    private static SkiaSharp.SKBitmap RenderSdkEntry(PluginSdk.FolderEntry e, int slot, int keySize)
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
        return BitmapHelper.RenderFolderEntry(core, null, slot, keySize, keySize, FolderConstants.Columns);
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
                        bmp = BitmapHelper.RenderFolderBackButton(config, slot, DeviceKeySize, DeviceKeySize, FolderConstants.Columns);
                    }
                    else if (folderNav.CurrentEntries.TryGetValue(slot, out var entry))
                    {
                        bmp = BitmapHelper.RenderFolderEntry(entry, config, slot, DeviceKeySize, DeviceKeySize, FolderConstants.Columns);
                    }
                    else
                    {
                        bmp = BitmapHelper.RenderEmptyFolderSlot(config, slot, DeviceKeySize, DeviceKeySize, FolderConstants.Columns);
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
        var newPage = (newIndex >= 0 && newIndex < config.TouchButtonPages.Count)
            ? config.TouchButtonPages[newIndex]
            : null;

        // Move the per-button ItemChanged wiring onto the newly active page (detaches the
        // previously wired page, which may belong to a different workspace after a switch).
        AttachTouchItemChanged(newPage);

        if (newPage != null)
        {
            foreach (var touchButton in newPage.TouchButtons)
            {
                // Reset stateful buttons that opt into per-page reset back to their default state
                // as the page becomes active. App-focus switches also flow through page changes,
                // so this transitively covers "reset on active-app change". External (plugin-driven)
                // buttons keep their state.
                if (touchButton.ResetOnPageChange && touchButton.Mode == ButtonStateMode.Local)
                    touchButton.ResetToDefaultState();
            }
        }

        // The side strips share the page wallpaper, so repaint them for the new page.
        if (!_isDeviceOff && !folderNav.IsActive && !exclusiveMode.Owns(ExclusiveControlScope.SideDisplays))
            _ = RedrawSideStrips();
    }

    // Tracks which touch-page collection our CollectionChanged/PropertyChanged handlers are
    // currently attached to, so a workspace switch can detach from the old workspace's pages
    // before attaching to the new one (issue #132). The active workspace's TouchButtonPages is a
    // different collection instance per workspace, so binding once at init is not enough.
    private System.Collections.ObjectModel.ObservableCollection<TouchButtonPage> _boundTouchPages;

    /// <summary>
    /// (Re)subscribes the controller's touch-page handlers to the active workspace's pages: the
    /// per-page wallpaper <see cref="TouchButtonPageOnPropertyChanged"/> and the collection's
    /// <see cref="TouchButtonPagesOnCollectionChanged"/>. Detaches from the previously bound
    /// collection first, so activating a different workspace/profile moves the wiring onto the new
    /// pages. Idempotent — a no-op when already bound to the active collection.
    /// </summary>
    public void BindActiveWorkspaceTouchPages()
    {
        var pages = config.TouchButtonPages;
        if (ReferenceEquals(pages, _boundTouchPages)) return;

        if (_boundTouchPages != null)
        {
            _boundTouchPages.CollectionChanged -= TouchButtonPagesOnCollectionChanged;
            foreach (var page in _boundTouchPages)
                page.PropertyChanged -= TouchButtonPageOnPropertyChanged;
        }

        _boundTouchPages = pages;

        if (_boundTouchPages != null)
        {
            foreach (var page in _boundTouchPages)
                page.PropertyChanged += TouchButtonPageOnPropertyChanged;
            _boundTouchPages.CollectionChanged += TouchButtonPagesOnCollectionChanged;
        }
    }

    // The touch page whose buttons currently have ItemChanged wired. Tracked so page/workspace
    // switches move the per-button redraw wiring cleanly (issue #132) instead of leaking
    // subscriptions on the previously active page.
    private TouchButtonPage _itemChangedPage;

    /// <summary>Wires <see cref="TouchItemChanged"/> onto the given page's buttons, detaching the
    /// previously wired page first. Idempotent when the page is already wired.</summary>
    private void AttachTouchItemChanged(TouchButtonPage page)
    {
        if (ReferenceEquals(page, _itemChangedPage)) return;
        DetachTouchItemChanged();
        if (page?.TouchButtons == null) return;
        foreach (var touchButton in page.TouchButtons)
            touchButton.ItemChanged += TouchItemChanged;
        _itemChangedPage = page;
    }

    private void DetachTouchItemChanged()
    {
        if (_itemChangedPage?.TouchButtons != null)
            foreach (var touchButton in _itemChangedPage.TouchButtons)
                touchButton.ItemChanged -= TouchItemChanged;
        _itemChangedPage = null;
    }

    /// <summary>
    /// Re-applies and repaints the active workspace after a profile/workspace switch (issue #132):
    /// rebinds the touch-page handlers, re-seeds the rotary pages, selects the workspace's startup
    /// touch page (forcing a redraw even when the remembered index is unchanged) and repaints the
    /// touch display and side strips. No device I/O happens while the device is off or another mode
    /// owns the screen. Must run on the UI thread.
    /// </summary>
    public async Task ApplyActiveWorkspace()
    {
        // The workspace being left may own a macro that is waiting for its trigger to come up (#185).
        ReleaseAllPresses();

        // A profile/workspace switch ends any full-display takeover (issue #124) and exclusive mode:
        // the takeover belonged to the workspace we are leaving. Neither auto-restarts — the owning
        // plugin re-enters explicitly via its own command.
        StopDisplayTakeovers();

        BindActiveWorkspaceTouchPages();
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

            // The freshly activated workspace remembers its own current index; reset it so
            // ApplyTouchPage (which early-returns when the index is unchanged) always repaints.
            pageManager.CurrentTouchPageIndex = -1;
            await pageManager.ApplyTouchPage(startupIndex, true);
        }

        config.CurrentRotaryButtonPage?.Selected = true;
        config.CurrentTouchButtonPage?.Selected = true;

        if (!_isDeviceOff && !folderNav.IsActive && !exclusiveMode.Owns(ExclusiveControlScope.SideDisplays))
            await RedrawSideStrips();
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

        // Folder mode, exclusive mode, or the screensaver owns the touch display —
        // suppress per-button redraws (e.g. from DynamicTextManager) so they don't paint
        // over the active view. When the owner exits, its handler repaints the page.
        if (folderNav.IsActive || exclusiveMode.Owns(ExclusiveControlScope.TouchButtons) ||
            _screensaverActive || _fullDisplayActive) return;

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
        if (_isDeviceOff || folderNav.IsActive || exclusiveMode.Owns(ExclusiveControlScope.SideDisplays) ||
            _screensaverActive || _fullDisplayActive) return;

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
    /// any additional slots (e.g. Razer's BUTTON4–BUTTON7, the CT's 12 named buttons) are
    /// created blank for the user to assign — preserves saved bindings via
    /// SimpleButtonExtensions.FindById.
    /// </summary>
    private async Task<SimpleButton[]> BuildSimpleButtons()
    {
        var device = deviceService.Device;

        // The Loupedeck CT has 8 round + 12 named square buttons (20 total, sized via
        // its own Buttons[] in LoupedeckCtDevice) — give the 8 round ones the same
        // numeric-page-selector defaults as Razer/Live, and leave the 12 named ones
        // blank since their physical labels (home/undo/save/...) don't map to an
        // obvious default command.
        if (device is LoupedeckDevice.Device.LoupedeckCtDevice)
        {
            var ctDefaults = new (Constants.ButtonType Id, string Cmd)[]
            {
                (Constants.ButtonType.BUTTON0, "System.GotoPage(1)"),
                (Constants.ButtonType.BUTTON1, "System.GotoPage(2)"),
                (Constants.ButtonType.BUTTON2, "System.GotoPage(3)"),
                (Constants.ButtonType.BUTTON3, "System.GotoPage(4)"),
                (Constants.ButtonType.BUTTON4, "System.GotoPage(5)"),
                (Constants.ButtonType.BUTTON5, "System.GotoPage(6)"),
                (Constants.ButtonType.BUTTON6, "System.GotoPage(7)"),
                (Constants.ButtonType.BUTTON7, "System.NextPage"),
                (Constants.ButtonType.CT_HOME, null),
                (Constants.ButtonType.CT_UNDO, null),
                (Constants.ButtonType.CT_KEYBOARD, null),
                (Constants.ButtonType.CT_ENTER, null),
                (Constants.ButtonType.CT_SAVE, null),
                (Constants.ButtonType.CT_FN_L, null),
                (Constants.ButtonType.CT_A, null),
                (Constants.ButtonType.CT_B, null),
                (Constants.ButtonType.CT_C, null),
                (Constants.ButtonType.CT_D, null),
                (Constants.ButtonType.CT_FN_R, null),
                (Constants.ButtonType.CT_E, null)
            };

            var ctCount = device.Buttons?.Length ?? 0;
            var ctResult = new SimpleButton[ctCount];
            for (var i = 0; i < ctCount && i < ctDefaults.Length; i++)
            {
                ctResult[i] = await CreateSimpleButton(ctDefaults[i].Id, Avalonia.Media.Colors.Blue, ctDefaults[i].Cmd ?? string.Empty);
            }
            return ctResult;
        }

        // The eight-button devices (Razer Stream Controller and Loupedeck Live, which
        // subclasses it) default their buttons to numeric profile (touch-page) selectors;
        // the six-button Live S keeps the classic page-nav layout.
        var defaults = device is LoupedeckDevice.Device.RazerStreamControllerDevice
            ? new (Constants.ButtonType Id, string Cmd)[]
            {
                (Constants.ButtonType.BUTTON0, "System.GotoPage(1)"),
                (Constants.ButtonType.BUTTON1, "System.GotoPage(2)"),
                (Constants.ButtonType.BUTTON2, "System.GotoPage(3)"),
                (Constants.ButtonType.BUTTON3, "System.GotoPage(4)"),
                (Constants.ButtonType.BUTTON4, "System.GotoPage(5)"),
                (Constants.ButtonType.BUTTON5, "System.GotoPage(6)"),
                (Constants.ButtonType.BUTTON6, "System.GotoPage(7)"),
                (Constants.ButtonType.BUTTON7, "System.NextPage")
            }
            : new (Constants.ButtonType Id, string Cmd)[]
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
        // The screensaver clip lives in the asset folder too — keep it referenced so
        // the save-time cleanup doesn't delete it (issue #120).
        if (!string.IsNullOrWhiteSpace(config.ScreensaverVideoPath))
            yield return config.ScreensaverVideoPath;

        // Schema-agnostic scan of the just-saved config file itself, exactly like
        // HarvestAssetPathsFromOtherConfigs below already does for sibling device configs.
        // This used to be a manual walk of config.TouchButtonPages/RotaryButtonPages, which
        // since #132 only reflect the ACTIVE profile's active workspace — every other
        // profile's images were invisible to it and got deleted as "orphans" on every save.
        // Scanning the serialized JSON for any "assets/…" string covers every profile,
        // workspace, and future asset-referencing field without having to keep this in
        // lockstep with the config schema.
        foreach (var path in AssetPathHarvester.HarvestFromFile(_configPath))
            yield return path;

        // The asset folder is shared by EVERY per-device config file
        // (config_<slug>[_<serial>].json) in the same config dir, but the live
        // `config` object only represents the active device. Without also honouring
        // the other devices' configs, this device's startup cleanup would delete
        // wallpapers/images that are still referenced by another device's config —
        // even though their AssetPath is intact on disk. Harvest those references too.
        foreach (var path in HarvestAssetPathsFromOtherConfigs())
            yield return path;
    }

    /// <summary>
    /// Scans sibling config files (all <c>config*.json</c> in the config dir except
    /// the one this controller owns) and returns every stored asset-relative path
    /// found anywhere in them, via <see cref="AssetPathHarvester.HarvestFromFile"/>.
    /// </summary>
    private IEnumerable<string> HarvestAssetPathsFromOtherConfigs()
    {
        string configDir;
        try
        {
            configDir = FileDialogHelper.GetConfigDir();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Asset cleanup: could not resolve config dir: {ex.Message}");
            yield break;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(configDir, "config*.json", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Asset cleanup: could not enumerate config files: {ex.Message}");
            yield break;
        }

        foreach (var file in files)
        {
            // The active config's references already came from the direct scan above.
            if (string.Equals(Path.GetFullPath(file), Path.GetFullPath(_configPath), StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var path in AssetPathHarvester.HarvestFromFile(file))
                yield return path;
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

                case nameof(LoupedeckConfig.DitheringEnabled):
                    // Dithering is applied while converting a bitmap to the framebuffer, so
                    // nothing already on the panel changes by itself — repaint what is visible.
                    // RedrawCurrentTouchPage covers the touch grid and the side strips, and
                    // no-ops while another owner (device-off, folder, exclusive mode,
                    // screensaver) holds the screen; that owner repaints when it releases.
                    // ApplyTouchPage cannot be used here: it early-returns when the requested
                    // page is already current, which is always the case for a settings toggle.
                    deviceService.Device.DitherFramebuffer = config.DitheringEnabled;
                    await RedrawCurrentTouchPage();
                    break;
            }
        }
        catch (TaskCanceledException)
        {
            // ignore canceled Tasks
        }
    }
}