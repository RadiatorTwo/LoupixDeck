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
            var deviceInfo = ActiveDeviceResolver.Resolve();

            if (deviceInfo == null)
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
                        await InitializeMainWindow(vm.SelectedDevice.Path, vm.SelectedBaudRate, vm.SelectedDevice.Info, desktop);
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
                await InitializeMainWindow(null, 0, deviceInfo, desktop);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task InitializeMainWindow(string port, int baudRate, DeviceRegistry.DeviceInfo deviceInfo,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        var splashScreen = new SplashScreen();
        desktop.MainWindow = splashScreen;
        splashScreen.Show();

        try
        {
            var viewModel = await CreateMainWindowViewModel(deviceInfo, port, baudRate);
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

    private async Task<MainWindowViewModel> CreateMainWindowViewModel(DeviceRegistry.DeviceInfo deviceInfo,
        string port = null, int baudrate = 0)
    {
        var collection = new ServiceCollection();
        collection.AddCommonServices(deviceInfo);

        var services = collection.BuildServiceProvider();

        services.PostInit();

        // Discover and load plugins before the command registry is built
        // (MainWindowViewModel's constructor initializes it), so plugin
        // commands are picked up alongside the core commands.
        services.GetRequiredService<Services.Plugins.IPluginManager>().LoadPlugins();

        // Build the side-strip provider lookup from the freshly loaded plugins so the
        // editor picker and the controller can resolve plugin-override strip bindings.
        services.GetRequiredService<Services.Plugins.ISideStripProviderRegistry>().Rebuild();

        // Expose the DI container so the CLI command channel in Program.cs can
        // resolve ICommandService for incoming "loupixdeck show / wakeup / …".
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
        ActiveDeviceResolver.RememberActive(deviceInfo);

        // Start dynamic-text providers AFTER the controller has wired up its
        // ItemChanged subscribers and drawn the initial page. Otherwise the very
        // first text update fires Refresh() before anyone is listening, and the
        // Text-setter equality check then suppresses subsequent updates.
        services.GetRequiredService<IDynamicTextManager>().Start();

        return mainViewModel;
    }

}
