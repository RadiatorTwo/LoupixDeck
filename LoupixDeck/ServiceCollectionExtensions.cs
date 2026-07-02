using LoupixDeck.Controllers;
using LoupixDeck.Models;
using LoupixDeck.Registry;
using LoupixDeck.Services;
using LoupixDeck.Services.ActiveWindow;
using LoupixDeck.Services.AppSwitching;
using LoupixDeck.Services.Commands;
using LoupixDeck.Services.FolderNavigation;
using LoupixDeck.Services.Macros;
using LoupixDeck.Services.Mouse;
using LoupixDeck.Services.Plugins;
using LoupixDeck.Services.SystemPower;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels;
using LoupixDeck.Views;
using Microsoft.Extensions.DependencyInjection;

namespace LoupixDeck;

/// <summary>
/// DI wiring for the issue #116 root + per-device topology.
///
/// <see cref="AddRootServices"/> registers the device-agnostic singletons (OS input,
/// config/asset IO, macro store, plugin discovery). <see cref="AddDeviceServices"/>
/// builds one child collection per device: it forwards the root singletons in via
/// <see cref="Forward{T}"/> and registers everything device-bound — including the
/// command catalog and the plugin-host wiring — so command activation and plugin
/// delegates resolve through THIS device's provider. Phase 1 instantiates exactly one
/// device; Phase 2 starts N child providers.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Re-expose a root-container singleton inside a device child collection.
    /// The instance stays the single root instance; only the resolution is forwarded.</summary>
    private static void Forward<T>(this IServiceCollection collection, IServiceProvider root)
        where T : class
        => collection.AddSingleton(_ => root.GetRequiredService<T>());

    // ───────────────────────── Root (device-agnostic) ─────────────────────────

    public static void AddRootServices(this IServiceCollection collection)
    {
        // Registry of every device brought up this session — process-wide concerns
        // (quit → shut down all devices' plugins) and phase-3 UI/CLI reach devices through it.
        collection.AddSingleton<IDeviceHostRegistry, DeviceHostRegistry>();

        // Routes the shared plugins' host calls to the device that triggered them.
        collection.AddSingleton<IDeviceRouter, DeviceRouter>();

        // Plugins are loaded once (shared instances); per-call device targeting is via
        // the router. Loading natively-interop deps (e.g. NAudio/COM) per device would
        // clash across collectible load contexts, so a single shared load is required.
        collection.AddSingleton<IPluginManager, PluginManager>();

        collection.AddSingleton<IConfigService, ConfigService>();
        collection.AddSingleton<IAssetService, AssetService>();

        // "Start with Windows" toggle (settings). Registry-backed on Windows, no-op elsewhere.
        collection.AddSingleton<IAutostartService, AutostartService>();

        // Animated-button assets (issue #121): decode-once frame cache and the import/transcode
        // pipeline are device-agnostic, so they live as shared root singletons (one decode shared
        // across devices) and are forwarded into each device provider.
        collection.AddSingleton<Services.Animation.IAnimatedImageCache, Services.Animation.AnimatedImageCache>();
        collection.AddSingleton<Services.Animation.IAnimatedImageImporter, Services.Animation.AnimatedImageImporter>();

        collection.AddSingleton<IDBusController, DBusController>();
        collection.AddSingleton<ICommandRunner, CommandRunner>();

        if (OperatingSystem.IsLinux())
            collection.AddSingleton<ISystemPowerService, LinuxSystemPowerService>();
#if WINDOWS
        else if (OperatingSystem.IsWindows())
            collection.AddSingleton<ISystemPowerService, WindowsSystemPowerService>();
#endif
        else
            collection.AddSingleton<ISystemPowerService, NoOpSystemPowerService>();

        // Foreground-window monitor. The Linux monitor needs no #if guard (it only uses
        // Process + /proc); only the Windows type lives behind #if WINDOWS.
        if (OperatingSystem.IsLinux())
            collection.AddSingleton<IActiveWindowMonitor, LinuxActiveWindowMonitor>();
#if WINDOWS
        else if (OperatingSystem.IsWindows())
            collection.AddSingleton<IActiveWindowMonitor, WindowsActiveWindowMonitor>();
#endif
        else
            collection.AddSingleton<IActiveWindowMonitor, NoOpActiveWindowMonitor>();

        // User-defined macros: in-memory store (macros.json), shared across devices.
        collection.AddSingleton<IMacroManager, MacroManager>();

        // App-global macro cancellation: per-device runners register here so the global
        // stop hotkey (a single process-wide listener) can cancel macros on every device.
        collection.AddSingleton<IMacroStopCoordinator, MacroStopCoordinator>();
        collection.AddSingleton<IMacroStopHotkeyService, MacroStopHotkeyService>();

        // Cached foreground-window snapshot + macro condition evaluator (used by If / Wait
        // steps). Shared app-wide so they observe a single monitor and process table.
        collection.AddSingleton<IActiveWindowState, ActiveWindowState>();
        collection.AddSingleton<IMacroConditionEvaluator, MacroConditionEvaluator>();

        // App-wide execution-mode gatekeeper: enforces Run Once / Restart / Parallel across
        // every device (macros are keyed by name, which can be bound on multiple devices).
        collection.AddSingleton<IMacroExecutionRegistry, MacroExecutionRegistry>();

        // Runtime text-input prompts for Prompt steps (shown on the UI thread).
        collection.AddSingleton<IMacroPromptService, MacroPromptService>();

        // Runtime USB hot-plug (issue #116 phase 3b): the OS-native watcher signals
        // topology changes; the manager diffs them against the running device set and
        // raises attach/detach events App turns into provider/VM bring-up + teardown.
        collection.AddSingleton<Services.HotPlug.IDeviceWatcher>(_ => Services.HotPlug.DeviceWatcher.Create());
        collection.AddSingleton<Services.HotPlug.IHotPlugManager, Services.HotPlug.HotPlugManager>();
    }

    // ───────────────────────── Device (per-device child) ─────────────────────────

    public static void AddDeviceServices(this IServiceCollection collection, ResolvedDevice resolved,
        IServiceProvider root)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(root);
        var deviceInfo = resolved.Info;

        collection.AddSingleton(resolved);
        collection.AddSingleton(deviceInfo);

        // Re-expose the root singletons device-bound services depend on.
        collection.Forward<IConfigService>(root);
        collection.Forward<IAssetService>(root);
        collection.Forward<IAutostartService>(root);
        collection.Forward<IDBusController>(root);
        collection.Forward<ICommandRunner>(root);
        collection.Forward<ISystemPowerService>(root);
        collection.Forward<IActiveWindowMonitor>(root);
        collection.Forward<IActiveWindowState>(root);
        collection.Forward<IMacroConditionEvaluator>(root);
        collection.Forward<IMacroExecutionRegistry>(root);
        collection.Forward<IMacroPromptService>(root);
        collection.Forward<IMacroManager>(root);
        collection.Forward<IMacroStopCoordinator>(root);
        collection.Forward<IDeviceHostRegistry>(root);
        collection.Forward<IDeviceRouter>(root);
        collection.Forward<IPluginManager>(root);
        collection.Forward<Services.Animation.IAnimatedImageCache>(root);
        collection.Forward<Services.Animation.IAnimatedImageImporter>(root);

        // OS input injection. Device-bound because the Windows routers read this
        // device's LoupedeckConfig.InterceptionEnabled to pick SendInput vs the
        // Interception driver per call.
        if (OperatingSystem.IsLinux())
        {
            collection.AddSingleton<IUInputKeyboard, UInputKeyboard>();
            // No Interception on Linux — register a stand-in so SettingsViewModel still resolves.
            collection.AddSingleton<IInterceptionService, NoOpInterceptionService>();

            // Virtual mouse for macro mouse steps (uinput-backed).
            collection.AddSingleton<IVirtualMouse, UInputMouse>();

            // Global keyboard recorder for the macro editor (evdev /dev/input/event*).
            collection.AddSingleton<IInputRecorder, LinuxInputRecorder>();
        }
        else
        {
            // Two concrete keyboard backends plus a router that picks between them per call:
            // SendInput (always works) and Interception (kernel driver, reaches raw-input apps).
            collection.AddSingleton<WindowsUInputKeyboard>();
            collection.AddSingleton<InterceptionKeyboard>();
            collection.AddSingleton<IUInputKeyboard, WindowsKeyboardRouter>();

            // Manages downloading/installing/uninstalling the Interception driver (settings page).
            collection.AddSingleton<IInterceptionService, InterceptionService>();

            // Virtual mouse for macro mouse steps — same backend split as the keyboard:
            // SendInput (always works) and Interception, picked per call by a router.
            collection.AddSingleton<WindowsVirtualMouse>();
            collection.AddSingleton<InterceptionMouse>();
            collection.AddSingleton<IVirtualMouse, WindowsMouseRouter>();

            // Global keyboard recorder for the macro editor (low-level hook).
            collection.AddSingleton<IInputRecorder, WindowsInputRecorder>();
        }

        collection.AddSingleton(provider =>
        {
            var configService = provider.GetRequiredService<IConfigService>();
            var configPath = FileDialogHelper.GetConfigPath(deviceInfo, resolved.Serial);
            var config = configService.LoadConfig<LoupedeckConfig>(configPath);
            if (config == null)
            {
                config = new LoupedeckConfig
                {
                    DeviceVid = deviceInfo.VendorId,
                    DevicePid = deviceInfo.ProductId,
                    DeviceSerial = resolved.Serial
                };

                // First launch for this device — seed the serial port/baud from any
                // existing sibling config so the user does not have to re-run InitSetup
                // just because they switched device type (the port is hardware, not
                // device-type-specific). Crucial for the LOUPIXDECK_FAKE_DEVICE flow:
                // without this the fresh config has no port → device times out →
                // App.InitializeDevices catches and shuts down silently.
                SeedSerialPortFromSibling(config, configService, deviceInfo);
            }
            return config;
        });

        collection.AddSingleton<IDeviceService, LoupedeckDeviceService>();
        collection.AddSingleton<IPageManager, PageManager>();

        // Command catalog — device-scoped so command activation
        // (SysCommandService → ActivatorUtilities.CreateInstance(this provider))
        // resolves the device-bound services commands inject.
        collection.AddSingleton<ICommandService, CommandService>();
        collection.AddSingleton<ICommandBuilder, CommandBuilder>();
        collection.AddSingleton<ISysCommandService, SysCommandService>();
        collection.AddSingleton<ICommandProvider, CoreCommandProvider>();
        collection.AddSingleton<ICommandProvider, PluginCommandProvider>();
        collection.AddSingleton<ICommandRegistry, CommandRegistry>();

        // The command-selection menu is assembled generically from these contributors.
        collection.AddSingleton<IMenuContributor, CommandGroupMenuContributor>();
        collection.AddSingleton<IMenuContributor, UserMacroMenuContributor>();
        collection.AddSingleton<IPluginMenuSource, PluginMenuContributor>();
        collection.AddSingleton<IMenuTreeBuilder, MenuTreeBuilder>();

        // Sequential macro-step executor (uses this device's command service).
        collection.AddSingleton<MacroRunner>();

        // Per-device plugin state: side-strip attachment (reads the shared root
        // plugin list), install/enable, hot-reload.
        collection.AddSingleton<ISideStripProviderRegistry, SideStripProviderRegistry>();
        collection.AddSingleton<IPluginInstaller, PluginInstaller>();
        collection.AddSingleton<IPluginReloadService, PluginReloadService>();

        // One central animation loop per device (issue #119): every animated feature
        // registers here instead of owning its own timer, so the device has a single,
        // globally rate-limitable render cadence that pauses for inactive pages.
        collection.AddSingleton<Services.Animation.IAnimationScheduler, Services.Animation.AnimationScheduler>();

        // Idle-driven full-display screensaver (issue #120): plays a video/GIF via ffmpeg
        // across all displays after an idle period, registering as a source on the scheduler.
        collection.AddSingleton<Services.Screensaver.IScreensaverManager, Services.Screensaver.ScreensaverManager>();

        collection.AddSingleton<IDynamicTextManager, DynamicTextManager>();

        // Lets plugins query/set the active state of stateful touch buttons (issue #131).
        collection.AddSingleton<IButtonStateService, ButtonStateService>();

        // Per-button animations (issue #121): one source per device on the central scheduler,
        // driving animated image layers and animated plugin commands.
        collection.AddSingleton<Services.Animation.IButtonAnimationManager, Services.Animation.ButtonAnimationManager>();

        collection.AddSingleton<IFolderNavigationService, FolderNavigationService>();
        collection.AddSingleton<IExclusiveModeService, ExclusiveModeService>();
        collection.AddSingleton<INativeHapticService, NativeHapticService>();
        collection.AddSingleton<IAppSwitchingService, AppSwitchingService>();

        collection.AddSingleton<LoupedeckLiveSController>();
        collection.AddSingleton<IDeviceController>(sp => sp.GetRequiredService<LoupedeckLiveSController>());

        collection.AddTransient<MainWindowViewModel>();

        InitDialogs(collection);
    }

    private static void SeedSerialPortFromSibling(LoupedeckConfig fresh, IConfigService configService,
        DeviceRegistry.DeviceInfo self)
    {
        try
        {
            var candidates = DeviceRegistry.SupportedDevices
                .Where(d => d.Slug != self.Slug)
                .Select(static d => FileDialogHelper.GetConfigPath(d))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();

            foreach (var path in candidates)
            {
                var sibling = configService.LoadConfig<LoupedeckConfig>(path);
                if (sibling == null || string.IsNullOrEmpty(sibling.DevicePort)) continue;
                fresh.DevicePort = sibling.DevicePort;
                fresh.DeviceBaudrate = sibling.DeviceBaudrate;
                Console.WriteLine($"[Config] Seeded {self.Slug} port from sibling: {sibling.DevicePort} @ {sibling.DeviceBaudrate}");
                return;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config] Sibling-port seed failed: {ex.Message}");
        }
    }

    private static void InitDialogs(IServiceCollection collection)
    {
        collection.AddTransient<SimpleButtonSettings>();
        collection.AddTransient<SimpleButtonSettingsViewModel>();

        collection.AddTransient<RotaryButtonSettings>();
        collection.AddTransient<RotaryButtonSettingsViewModel>();

        collection.AddTransient<TouchButtonSettings>();
        collection.AddTransient<TouchButtonSettingsViewModel>();

        collection.AddTransient<SymbolPicker>();
        collection.AddTransient<SymbolPickerViewModel>();

        collection.AddTransient<TouchPageWallpaperSettings>();
        collection.AddTransient<TouchPageWallpaperSettingsViewModel>();

        collection.AddTransient<PageCommandsSettings>();
        collection.AddTransient<PageCommandsSettingsViewModel>();

        collection.AddTransient<Settings>();
        collection.AddTransient<SettingsViewModel>();

        collection.AddTransient<MacroEditor>();
        collection.AddTransient<MacroEditorViewModel>();

        collection.AddTransient<About>();
        collection.AddTransient<AboutViewModel>();

        collection.AddSingleton<IDialogService, DialogService>();
    }

    /// <summary>Root-level one-time init: shared macro store + the static bitmap
    /// renderer's asset resolver. Runs once on the root provider.</summary>
    public static void RootPostInit(this IServiceProvider root)
    {
        // Load user macros once — execution and menus read from memory afterwards.
        root.GetRequiredService<IMacroManager>().Load();

        // Begin listening for the global stop hotkey (no-op until one is configured).
        root.GetRequiredService<IMacroStopHotkeyService>().Start();

        // Eagerly create the active-window cache so it subscribes to (and starts) the monitor
        // from launch — otherwise it would miss every focus change before the first macro runs.
        root.GetRequiredService<IActiveWindowState>();

        // Let the (static) bitmap renderer resolve image-layer assets via DI.
        var assetService = root.GetRequiredService<IAssetService>();
        BitmapHelper.AssetResolver = assetService.Load;
    }

    /// <summary>Per-device one-time init: dialog registration, haptic materialization,
    /// config self-heal, layer-handler rewiring. Runs on each device provider.</summary>
    public static void DevicePostInit(this IServiceProvider services)
    {
        var dialogService = services.GetRequiredService<IDialogService>();

        dialogService.Register<SimpleButtonSettingsViewModel, SimpleButtonSettings>();
        dialogService.Register<RotaryButtonSettingsViewModel, RotaryButtonSettings>();
        dialogService.Register<TouchButtonSettingsViewModel, TouchButtonSettings>();
        dialogService.Register<SymbolPickerViewModel, SymbolPicker>();
        dialogService.Register<TouchPageWallpaperSettingsViewModel, TouchPageWallpaperSettings>();
        dialogService.Register<PageCommandsSettingsViewModel, PageCommandsSettings>();
        dialogService.Register<SettingsViewModel, Settings>();
        dialogService.Register<MacroEditorViewModel, MacroEditor>();
        dialogService.Register<AboutViewModel, About>();

        // Heal configs that were saved before HapticSteps had ObjectCreationHandling.Replace —
        // those files accumulated duplicate steps on every save+load round.
        var hapticConfig = services.GetRequiredService<LoupedeckConfig>();
        while (hapticConfig.HapticSteps.Count > SettingsViewModel.MaxHapticSteps)
            hapticConfig.HapticSteps.RemoveAt(hapticConfig.HapticSteps.Count - 1);
        if (hapticConfig.HapticSteps.Count == 0)
            hapticConfig.HapticSteps.Add(new HapticStep());

        // Materialize the haptic service so it subscribes to config/page events,
        // and push the persisted config to the device once it's connected.
        services.GetRequiredService<INativeHapticService>().Apply();

        // After config load, rewire per-layer PropertyChanged handlers so edits
        // trigger TouchButton.Refresh(). The collection setter in TouchButton
        // wires its own CollectionChanged hook, but layers created by the JSON
        // converter bypass AttachLayerHandlers.
        var config = services.GetRequiredService<LoupedeckConfig>();
        if (config.TouchButtonPages != null)
        {
            foreach (var page in config.TouchButtonPages)
            {
                if (page?.TouchButtons == null) continue;
                foreach (var button in page.TouchButtons)
                {
                    button?.RewireLayerHandlers();
                }
            }
        }

        // Normalize each LED button's active state + command mirror after load (no layers).
        if (config.SimpleButtons != null)
        {
            foreach (var button in config.SimpleButtons)
                button?.RewireAfterLoad();
        }
    }
}
