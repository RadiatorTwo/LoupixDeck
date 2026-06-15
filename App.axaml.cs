using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
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
    public override void Initialize()
    {
        Console.WriteLine($"App.Initialize {DateTime.Now:HH:mm:ss}");
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Decide which device's config we're booting with BEFORE building DI:
            // existing per-device file (preferred), legacy config.json (migrated),
            // or null → user must pick via InitSetup.
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
                        await InitializeMainWindow(vm.SelectedDevice.Path, vm.SelectedBaudRate, picked, desktop);
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
                await InitializeMainWindow(null, 0, resolved, desktop);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task InitializeMainWindow(string port, int baudRate, ResolvedDevice resolved,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        var splashScreen = new SplashScreen();
        desktop.MainWindow = splashScreen;
        splashScreen.Show();

        try
        {
            var viewModel = await CreateMainWindowViewModel(resolved, port, baudRate);
            OnViewModelCreated(viewModel, splashScreen, desktop);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"InitializeMainWindow failed: {ex}");
            desktop.Shutdown();
        }
    }

    private void OnViewModelCreated(MainWindowViewModel viewModel, SplashScreen splashScreen,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        // Use the UI thread to make changes to the UI
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            var mainWindow = new MainWindow
            {
                DataContext = viewModel
            };

            desktop.MainWindow = mainWindow;

            // Skip Show() entirely when starting minimized to tray, otherwise the
            // window briefly flashes onscreen before OnDataContextChanged hides it.
            // We also switch to OnExplicitShutdown so the lifetime doesn't end the
            // moment the splash closes with no visible window — the tray-icon is
            // the only entry point and Environment.Exit(0) is the only exit path.
            if (viewModel.LoupedeckController?.Config?.StartMinimizedToTray == true)
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

    private async Task<MainWindowViewModel> CreateMainWindowViewModel(ResolvedDevice resolved,
        string port = null, int baudrate = 0)
    {
        // Root container: device-agnostic singletons (OS input, config/asset IO,
        // macro store, plugin discovery). Built once for the app lifetime.
        var rootCollection = new ServiceCollection();
        rootCollection.AddRootServices();
        var root = rootCollection.BuildServiceProvider();
        root.RootPostInit();

        // Device child container: device-bound services + command catalog + plugin
        // host wiring, forwarding the root singletons in. One per device (phase 1: one).
        var deviceCollection = new ServiceCollection();
        deviceCollection.AddDeviceServices(resolved, root);
        var services = deviceCollection.BuildServiceProvider();

        // Plugin host delegates and root-resident lookups reach this device's services
        // through the holder — MUST be set before plugins load or any command executes.
        root.GetRequiredService<IActiveDeviceProvider>().Current = services;

        services.DevicePostInit();

        // Discover and load plugins before the command registry is built
        // (MainWindowViewModel's constructor initializes it), so plugin
        // commands are picked up alongside the core commands. Discovery is once,
        // in the root; the per-device hosts were wired via the holder above.
        root.GetRequiredService<Services.Plugins.IPluginManager>().LoadPlugins();

        // Build the side-strip provider lookup from the freshly loaded plugins so the
        // editor picker and the controller can resolve plugin-override strip bindings.
        services.GetRequiredService<Services.Plugins.ISideStripProviderRegistry>().Rebuild();

        // Expose the active device's container so the CLI command channel in Program.cs
        // can resolve ICommandService for incoming "loupixdeck show / wakeup / …".
        Program.AppServices = services;

        // Apply persisted theme variant before showing UI.
        var cfg = services.GetRequiredService<LoupedeckConfig>();
        RequestedThemeVariant = cfg.ThemeVariant switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

        var mainViewModel = services.GetRequiredService<MainWindowViewModel>();

        await mainViewModel.LoupedeckController.Initialize(port, baudrate);

        // Persist the just-booted device so the next launch can disambiguate
        // when multiple devices are plugged in (or none).
        ActiveDeviceResolver.RememberActive(resolved);

        // Start dynamic-text providers AFTER the controller has wired up its
        // ItemChanged subscribers and drawn the initial page. Otherwise the very
        // first text update fires Refresh() before anyone is listening, and the
        // Text-setter equality check then suppresses subsequent updates.
        services.GetRequiredService<IDynamicTextManager>().Start();

        return mainViewModel;
    }

}
