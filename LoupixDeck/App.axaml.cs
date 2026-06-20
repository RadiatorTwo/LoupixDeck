using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using LoupixDeck.Controllers;
using LoupixDeck.Models;
using LoupixDeck.Registry;
using LoupixDeck.Services;
using LoupixDeck.Services.AppSwitching;
using LoupixDeck.Services.Commands;
using LoupixDeck.Services.SystemPower;
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
        var splashScreen = new SplashScreen();
        desktop.MainWindow = splashScreen;
        splashScreen.Show();

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

            // Pass 1: build every device's child provider and register it.
            IServiceProvider primaryProvider = null;
            foreach (var device in devices)
            {
                var isPrimary = primary != null && device.ScopeKey == primary.ScopeKey;
                var provider = BuildDeviceProvider(device, root);
                var controller = provider.GetRequiredService<IDeviceController>();
                registry.Add(new DeviceHost(device, provider, controller, isPrimary));
                if (isPrimary) primaryProvider = provider;
            }

            // The router falls back to the primary for spontaneous plugin callbacks
            // (plugin timers/events with no active device flow). Set before loading.
            router.Default = primaryProvider ?? registry.Hosts.FirstOrDefault()?.Provider;

            // Load the shared plugin set ONCE (root) now that the fallback device is set.
            root.GetRequiredService<Services.Plugins.IPluginManager>().LoadPlugins();

            // Pass 2: build each device's side-strip lookup from the loaded plugins, build a
            // view model per device into the shell, and bring every device up. The VM ctor
            // wires the command registry / power / app-switching (StartMonitoring is idempotent),
            // so a secondary device needs no special headless path — only its own window tab.
            var shell = new MainShellViewModel();
            _shell = shell;
            foreach (var host in registry.Hosts)
            {
                host.Provider.GetRequiredService<Services.Plugins.ISideStripProviderRegistry>().Rebuild();

                var vm = host.Provider.GetRequiredService<MainWindowViewModel>();
                shell.Add(vm);

                if (host.IsPrimary)
                {
                    // Expose the primary's container so the CLI command channel resolves
                    // its ICommandService (phase 2: CLI targets the primary device).
                    Program.AppServices = host.Provider;
                    shell.SelectedDevice = vm;

                    var cfg = host.Provider.GetRequiredService<LoupedeckConfig>();
                    RequestedThemeVariant = cfg.ThemeVariant switch
                    {
                        "Light" => ThemeVariant.Light,
                        "Dark" => ThemeVariant.Dark,
                        _ => ThemeVariant.Default
                    };

                    await vm.LoupedeckController.Initialize(port, baudRate);
                    ActiveDeviceResolver.RememberActive(host.Device);
                }
                else
                {
                    await vm.LoupedeckController.Initialize(null, 0);
                }

                host.Provider.GetRequiredService<IDynamicTextManager>().Start();
            }

            OnViewModelCreated(shell, splashScreen, desktop);

            // Arm runtime hot-plug now that the initial device set is up.
            StartHotPlug();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"InitializeDevices failed: {ex}");
            desktop.Shutdown();
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
        registry.Add(new DeviceHost(device, provider, controller, becomesPrimary));

        provider.GetRequiredService<Services.Plugins.ISideStripProviderRegistry>().Rebuild();
        var vm = provider.GetRequiredService<MainWindowViewModel>();
        _shell.Add(vm);

        if (becomesPrimary)
        {
            Program.AppServices = provider;
            _root.GetRequiredService<IDeviceRouter>().Default = provider;
            _shell.SelectedDevice = vm;
            ActiveDeviceResolver.RememberActive(device);
        }

        await controller.Initialize(null, 0);
        provider.GetRequiredService<IDynamicTextManager>().Start();
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
    private static IServiceProvider BuildDeviceProvider(ResolvedDevice device, IServiceProvider root)
    {
        var collection = new ServiceCollection();
        collection.AddDeviceServices(device, root);
        var provider = collection.BuildServiceProvider();
        provider.DevicePostInit();
        return provider;
    }

    private void OnViewModelCreated(MainShellViewModel shell, SplashScreen splashScreen,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (shell?.SelectedDevice == null)
        {
            // No primary device resolved (shouldn't happen for a non-empty device set).
            splashScreen.Close();
            desktop.Shutdown();
            return;
        }

        // Use the UI thread to make changes to the UI
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            var mainWindow = new MainWindow
            {
                DataContext = shell
            };

            desktop.MainWindow = mainWindow;

            // Skip Show() entirely when starting minimized to tray, otherwise the
            // window briefly flashes onscreen before OnDataContextChanged hides it.
            // We also switch to OnExplicitShutdown so the lifetime doesn't end the
            // moment the splash closes with no visible window — the tray-icon is
            // the only entry point and Environment.Exit(0) is the only exit path.
            if (shell.SelectedDevice.LoupedeckController?.Config?.StartMinimizedToTray == true)
            {
                desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
                mainWindow.MarkStartedMinimized();
            }
            else
            {
                mainWindow.Show();
            }
            splashScreen.Close();
        });
    }
}
