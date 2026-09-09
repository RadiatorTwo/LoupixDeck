using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using LoupixDeck.Controllers;
using LoupixDeck.Models;
using LoupixDeck.Registry;
using LoupixDeck.Services;
using LoupixDeck.Views;
using LoupixDeck.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using LoupixDeck.Utils;

namespace LoupixDeck;

public partial class App : Application
{
    // Kept for runtime hot-plug (issue #116 phase 3b): attach/detach handlers need the
    // shared root provider, the shell that hosts per-device VMs, and the desktop lifetime.
    private IServiceProvider _root;
    private MainShellViewModel _shell;
    private IClassicDesktopStyleApplicationLifetime _desktop;

    public override void Initialize()
    {
        Console.WriteLine($"App.Initialize {DateTime.Now:HH:mm:ss}");
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            WireGracefulShutdown(desktop);

            // The device emulation is compiled out of Release builds, so a Release launch
            // ignores LOUPIXDECK_FAKE_DEVICE without a word. State the build and the value
            // once at startup — otherwise "the override does nothing" has three
            // indistinguishable causes.
#if DEBUG
            Console.WriteLine("[Startup] DEBUG build — device emulation available. " +
                              $"LOUPIXDECK_FAKE_DEVICE='{Environment.GetEnvironmentVariable("LOUPIXDECK_FAKE_DEVICE") ?? "<unset>"}'");
#else
            Console.WriteLine("[Startup] RELEASE build — device emulation is compiled out; " +
                              "LOUPIXDECK_FAKE_DEVICE has no effect.");
#endif

            // Bring up EVERY connected supported device in parallel (issue #116 phase 2).
            var connected = ActiveDeviceResolver.ResolveAll();

            if (connected.Count > 0)
            {
                var primary = ActiveDeviceResolver.PickPrimary(connected);
                await InitializeDevices(connected, primary, null, 0, desktop);
            }
            else
            {
                // Nothing connected — fall back to the existing per-device file /
                // legacy config.json / marker resolution; null → InitSetup.
                var resolved = ActiveDeviceResolver.Resolve();
                if (resolved == null)
                {
                    var initWindow = new InitSetup
                    {
                        DataContext = new InitSetupViewModel()
                    };

                    initWindow.Closed += async (_, _) =>
                    {
                        if (initWindow.DataContext is InitSetupViewModel { ConnectionWorking: true } vm
                            && vm.SelectedDevice?.Info != null)
                        {
                            var picked = new ResolvedDevice(vm.SelectedDevice.Info, vm.SelectedDevice.Serial);
                            await InitializeDevices([picked], picked, vm.SelectedDevice.Path, vm.SelectedBaudRate, desktop);
                        }
                        else
                        {
                            desktop.Shutdown();
                        }
                    };

                    initWindow.Show();
                }
                else
                {
                    await InitializeDevices([resolved], resolved, null, 0, desktop);
                }
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Build the shared root once, then bring up every device on its own child
    /// provider. The primary device gets the config window; the rest run headless.
    /// </summary>
    private async Task InitializeDevices(IReadOnlyList<ResolvedDevice> devices, ResolvedDevice primary,
        string port, int baudRate, IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            // Root container: device-agnostic singletons (OS-level IO, macro store,
            // config/asset IO, shared plugins, the running-device registry). Built once.
            var rootCollection = new ServiceCollection();
            rootCollection.AddRootServices();
            var root = rootCollection.BuildServiceProvider();
            root.RootPostInit();
            _root = root;
            _desktop = desktop;
            var registry = root.GetRequiredService<IDeviceHostRegistry>();
            var router = root.GetRequiredService<IDeviceRouter>();

            // Pass 1: build every device's child provider and register it. Each
            // device is isolated: a failure to build one must not take the others
            // (or the whole app) down with it (issue #146).
            IServiceProvider primaryProvider = null;
            foreach (var device in devices)
            {
                try
                {
                    var isPrimary = primary != null && device.ScopeKey == primary.ScopeKey;
                    var provider = BuildDeviceProvider(device, root);
                    var controller = provider.GetRequiredService<IDeviceController>();
                    registry.Add(new DeviceHost(device, provider, controller, isPrimary));
                    if (isPrimary) primaryProvider = provider;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Init] Failed to build device '{device.ScopeKey}': {ex}");
                }
            }

            if (registry.Hosts.Count == 0)
            {
                Console.WriteLine("[Init] No device could be brought up — shutting down.");
                desktop.Shutdown();
                return;
            }

            // The router falls back to the primary for spontaneous plugin callbacks
            // (plugin timers/events with no active device flow). Set before loading.
            router.Default = primaryProvider ?? (registry.Hosts.Count is > 0 ? registry.Hosts[0]?.Provider : null);

            var primaryHost = registry.Primary;
            // The shell owns the two menu entries that reach no hardware, About and Quit, so it
            // needs the dialog service. It comes from the primary device's container, which
            // exists as soon as a device is configured — connected or not.
            var shell = new MainShellViewModel(primaryHost?.Provider.GetService<IDialogService>());
            _shell = shell;

            // The window goes up here, before the plugins load and before a single device is
            // brought up, and it opens on the empty state it already has for a full unplug
            // (issue #191). Devices fill in as they connect. This is what a splash screen used
            // to cover: it existed because the window could not be built until a device had
            // finished initialising, so a slow plugin set or a device taking its time left the
            // screen blank. Neither is true any more — a device joins the shell whenever its
            // link comes up (see ShowWhenConnected), including seconds after the window opened.
            var primaryConfig = primaryHost?.Provider.GetService<LoupedeckConfig>();
            // Expose the primary's container so the CLI command channel resolves its
            // ICommandService (phase 2: CLI targets the primary device). Hoisted out of the
            // bring-up loop so quitting during bring-up still reaches the running devices.
            if (primaryHost != null)
                Program.AppServices = primaryHost.Provider;
            // Read before the window is built, otherwise it paints in the default theme and
            // then switches.
            RequestedThemeVariant = primaryConfig?.ThemeVariant switch
            {
                "Light" => ThemeVariant.Light,
                "Dark" => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };
            ShowMainWindow(shell, primaryConfig, desktop);

            // Load the shared plugin set ONCE (root) now that the fallback device is set.
            root.GetRequiredService<Services.Plugins.IPluginManager>().LoadPlugins();

            // Pass 2: build each device's side-strip lookup from the loaded plugins, build a
            // view model per device into the shell, and bring every device up. The VM ctor
            // wires the command registry / power / app-switching (StartMonitoring is idempotent),
            // so a secondary device needs no special headless path — only its own window tab.
            int broughtUp = 0;
            foreach (var host in registry.Hosts.ToList())
            {
                // Isolate each device's bring-up: a single device throwing here must
                // not abort the others or kill the app (issue #146).
                try
                {
                    host.Provider.GetRequiredService<Services.Plugins.ISideStripProviderRegistry>().Rebuild();
                    host.Provider.GetRequiredService<Services.Plugins.IScreensaverProviderRegistry>().Rebuild();

                    var vm = host.Provider.GetRequiredService<MainWindowViewModel>();

                    if (host.IsPrimary)
                    {
                        await vm.LoupedeckController.Initialize(port, baudRate);
                        ActiveDeviceResolver.RememberActive(host.Device);
                    }
                    else
                    {
                        await vm.LoupedeckController.Initialize(null, 0);
                    }

                    ShowWhenConnected(shell, host, vm);

                    host.Provider.GetRequiredService<IDynamicTextManager>().Start();
                    host.Provider.GetRequiredService<Services.Animation.IButtonAnimationManager>().Start();
                    host.Provider.GetRequiredService<Services.Animation.ISideDisplayAnimationManager>().Start();
                    host.Provider.GetRequiredService<Services.Animation.IWallpaperAnimationManager>().Start();
                    broughtUp++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Init] Failed to bring up device '{host.Device.ScopeKey}': {ex}");
                    registry.Remove(host);
                }
            }

            if (broughtUp == 0)
            {
                // Every device threw a non-transport failure — a config or model bug, since a
                // dead link no longer aborts a bring-up. Nothing left to drive the window with.
                Console.WriteLine("[Init] No device finished initialisation — shutting down.");
                desktop.Shutdown();
                return;
            }

            // Arm runtime hot-plug now that the initial device set is up.
            StartHotPlug();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"InitializeDevices failed: {ex}");
            desktop.Shutdown();
        }
    }

    /// <summary>
    /// Puts a device's view model into the shell once its serial link is actually up, and holds
    /// it back until then.
    ///
    /// Bringing a device up no longer proves it ever connected: a device whose port is briefly
    /// held by another process now finishes its in-memory bring-up so the window can open at all
    /// (PR #219). Its tab would otherwise sit in the device switcher backed by nothing — a layout
    /// that never lights up and a Profile/Workspace selector that reaches no hardware. The host
    /// stays registered either way, which is what lets the hot-plug reconciler keep taking the
    /// port over the moment it frees; that reconnect is what brings the tab in.
    /// </summary>
    private static void ShowWhenConnected(MainShellViewModel shell, DeviceHost host, MainWindowViewModel vm)
    {
        // Subscribed before the state is read, so a connect landing in between still shows up.
        host.Controller.DeviceConnected += (_, _) => Show();
        if (host.Controller.IsDeviceConnected)
            Show();
        else
            Console.WriteLine($"[Init] '{host.Device.ScopeKey}' is not connected — hidden until its port frees.");

        void Show()
        {
            // Invoke, not Post: on the bring-up path this already runs on the UI thread, and a
            // queued callback would land after the window has been built from the shell — which
            // reads the shell as empty and used to end the app.
            if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                AddToShell();
            else
                Avalonia.Threading.Dispatcher.UIThread.Post(AddToShell);
        }

        void AddToShell()
        {
            // Every connect raises the event, not just the first — a reconnect must not add
            // the device a second time.
            if (shell.Devices.Contains(vm)) return;
            shell.Add(vm);
            // Add() selects the first device by itself; the primary takes the selection back
            // when it arrives after a secondary.
            if (host.IsPrimary)
                shell.SelectedDevice = vm;
        }
    }

    // ──────── Runtime hot-plug (issue #116 phase 3b) ────────

    /// <summary>Subscribe to the hot-plug manager and start watching. Attach/detach
    /// land on a background timer thread, so each handler marshals to the UI thread
    /// before touching view models / the device.</summary>
    private void StartHotPlug()
    {
        var manager = _root.GetRequiredService<Services.HotPlug.IHotPlugManager>();

        manager.DeviceAttached += device =>
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                try { await AttachDeviceAsync(device); }
                catch (Exception ex) { Console.WriteLine($"[HotPlug] attach failed: {ex}"); }
            });

        manager.DeviceDetached += host =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                try { DetachDevice(host); }
                catch (Exception ex) { Console.WriteLine($"[HotPlug] detach failed: {ex}"); }
            });

        manager.Start();
    }

    /// <summary>Bring up a newly-connected device on its own child provider and add it
    /// to the shell. If it's the only device (all others were unplugged), it becomes
    /// the new primary (CLI/UI default + plugin fallback target). UI thread.</summary>
    private async Task AttachDeviceAsync(ResolvedDevice device)
    {
        var registry = _root.GetRequiredService<IDeviceHostRegistry>();
        if (registry.Find(device.ScopeKey) != null)
            return; // already running (raced reconcile)

        Console.WriteLine($"[HotPlug] attaching {device.Info.Name} ({device.ScopeKey})");

        var provider = BuildDeviceProvider(device, _root);
        var controller = provider.GetRequiredService<IDeviceController>();

        // First device after a full unplug → it owns the window again.
        var becomesPrimary = registry.Hosts.Count == 0;
        // Add synchronously (before any await) so a second reconcile can't double-attach.
        var host = new DeviceHost(device, provider, controller, becomesPrimary);
        registry.Add(host);

        provider.GetRequiredService<Services.Plugins.ISideStripProviderRegistry>().Rebuild();
        provider.GetRequiredService<Services.Plugins.IScreensaverProviderRegistry>().Rebuild();
        var vm = provider.GetRequiredService<MainWindowViewModel>();

        if (becomesPrimary)
        {
            Program.AppServices = provider;
            _root.GetRequiredService<IDeviceRouter>().Default = provider;
            ActiveDeviceResolver.RememberActive(device);
        }

        await controller.Initialize(null, 0);
        // A device can appear on the bus with its port still held by another process, so the
        // tab waits for the link exactly as it does at startup.
        ShowWhenConnected(_shell, host, vm);
        provider.GetRequiredService<IDynamicTextManager>().Start();
        provider.GetRequiredService<Services.Animation.IButtonAnimationManager>().Start();
        provider.GetRequiredService<Services.Animation.ISideDisplayAnimationManager>().Start();
        provider.GetRequiredService<Services.Animation.IWallpaperAnimationManager>().Start();
    }

    /// <summary>Tear a hot-unplugged device down: close its controller, stop its dynamic
    /// text loop, drop its VM + registry entry, and hand the primary role to a survivor
    /// if it owned it. The child provider is deliberately NOT disposed — Forward&lt;T&gt;
    /// would dispose the shared root singletons it re-exposes. UI thread.</summary>
    private void DetachDevice(DeviceHost host)
    {
        var registry = _root.GetRequiredService<IDeviceHostRegistry>();
        if (!registry.Hosts.Contains(host))
            return; // already detached (raced reconcile)
        Console.WriteLine($"[HotPlug] detaching {host.Device.Info.Name} ({host.Device.ScopeKey})");

        try { host.Controller.Shutdown(); }
        catch (Exception ex) { Console.WriteLine($"[HotPlug] controller shutdown failed: {ex.Message}"); }

        (host.Provider.GetService(typeof(IDynamicTextManager)) as IDisposable)?.Dispose();
        (host.Provider.GetService(typeof(Services.Animation.IButtonAnimationManager)) as IDisposable)?.Dispose();
        (host.Provider.GetService(typeof(Services.Animation.ISideDisplayAnimationManager)) as IDisposable)?.Dispose();
        (host.Provider.GetService(typeof(Services.Animation.IWallpaperAnimationManager)) as IDisposable)?.Dispose();
        (host.Provider.GetService(typeof(Services.Screensaver.IScreensaverManager)) as IDisposable)?.Dispose();
        (host.Provider.GetService(typeof(Services.Animation.IAnimationScheduler)) as IDisposable)?.Dispose();

        var vm = _shell.Devices.FirstOrDefault(v =>
            string.Equals(v.ScopeKey, host.Device.ScopeKey, StringComparison.OrdinalIgnoreCase));
        if (vm != null)
        {
            vm.Detach();
            _shell.Remove(vm);
        }

        var wasPrimary = ReferenceEquals(Program.AppServices, host.Provider);
        registry.Remove(host);

        if (!wasPrimary)
            return;

        // The window's owner is gone — promote a survivor.
        var next = registry.Primary;
        if (next == null)
            return;

        Program.AppServices = next.Provider;
        _root.GetRequiredService<IDeviceRouter>().Default = next.Provider;
        var nextVm = _shell.Devices.FirstOrDefault(v =>
            string.Equals(v.ScopeKey, next.Device.ScopeKey, StringComparison.OrdinalIgnoreCase));
        if (nextVm != null)
            _shell.SelectedDevice = nextVm;
        ActiveDeviceResolver.RememberActive(next.Device);
    }

    /// <summary>Build and prime a device's child provider: device services + command
    /// catalog. Plugins are loaded once at the root afterwards; side-strip lookup is
    /// rebuilt per device in pass 2.</summary>
    /// <summary>Keeps the SIGTERM handler alive for the process lifetime.</summary>
    private System.Runtime.InteropServices.PosixSignalRegistration _sigTerm;

    /// <summary>
    /// Routes every exit that is not the user clicking Quit through the same teardown, so the
    /// devices are always handed back blank. Windows ends a session with WM_ENDSESSION, which
    /// Avalonia surfaces as ShutdownRequested; Linux sends SIGTERM, which reaches no Avalonia
    /// event at all and would otherwise kill the process with the last page still lit.
    /// </summary>
    private void WireGracefulShutdown(IClassicDesktopStyleApplicationLifetime desktop)
    {
        desktop.ShutdownRequested += (_, _) => MainWindow.RequestQuit();

        try
        {
            _sigTerm = System.Runtime.InteropServices.PosixSignalRegistration.Create(
                System.Runtime.InteropServices.PosixSignal.SIGTERM,
                context =>
                {
                    // Own the exit: the default action would kill the process before the
                    // devices are blanked. The teardown touches the tray icon and the view
                    // models, so it runs on the UI thread; it ends the process itself.
                    context.Cancel = true;
                    Avalonia.Threading.Dispatcher.UIThread.Post(MainWindow.RequestQuit);
                });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Shutdown] SIGTERM handler unavailable: {ex.Message}");
        }
    }

    private static IServiceProvider BuildDeviceProvider(ResolvedDevice device, IServiceProvider root)
    {
        var collection = new ServiceCollection();
        collection.AddDeviceServices(device, root);
        var provider = collection.BuildServiceProvider();
        provider.DevicePostInit();
        return provider;
    }

    /// <summary>
    /// Builds and shows the one window, on an empty shell that devices join as they connect.
    /// <paramref name="startupConfig"/> is the primary device's config, read before any device
    /// has been brought up — the only setting needed this early is StartMinimizedToTray.
    /// </summary>
    private static void ShowMainWindow(MainShellViewModel shell, LoupedeckConfig startupConfig,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        // Invoke, not InvokeAsync: the caller is already on the UI thread, and a queued
        // operation would not run until the bring-up loop's first await — which is the wait
        // this window exists to cover.
        Avalonia.Threading.Dispatcher.UIThread.Invoke(() =>
        {
            var mainWindow = new MainWindow
            {
                DataContext = shell
            };

            // Skip Show() entirely when starting minimized to tray, otherwise the window
            // briefly flashes onscreen before it is hidden again. We also switch to
            // OnExplicitShutdown so the lifetime doesn't end with no visible window — the
            // tray icon is the only entry point and Environment.Exit(0) the only exit path.
            if (startupConfig?.StartMinimizedToTray == true)
            {
                desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
                mainWindow.MarkStartedMinimized();
                // The lifetime shows MainWindow itself once OnFrameworkInitializationCompleted
                // has returned — which is the first await of the device bring-up, i.e. after
                // this runs. Assigning the property now would therefore put the window on
                // screen against the user's setting. Posting the assignment lands it after
                // that implicit Show, which then finds no window and does nothing. This used
                // to be masked by the splash screen: it was the MainWindow at that moment and
                // was already visible, so the implicit Show was a no-op.
                Avalonia.Threading.Dispatcher.UIThread.Post(() => desktop.MainWindow = mainWindow);
            }
            else
            {
                // Assigned before Show() so the lifetime's own Show finds a visible window
                // and does nothing.
                desktop.MainWindow = mainWindow;
                mainWindow.Show();
            }
        });
    }
}
