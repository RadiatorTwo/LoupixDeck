using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using LoupixDeck.Views;
using LoupixDeck.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using LoupixDeck.Utils;

namespace LoupixDeck;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        DisableAvaloniaDataAnnotationValidation();

        var configPath = FileDialogHelper.GetConfigPath("config.json");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (!File.Exists(configPath))
            {
                var initWindow = new InitSetup
                {
                    DataContext = new InitSetupViewModel()
                };
                
                initWindow.Closed += async (_, _) =>
                {
                    if (initWindow.DataContext is InitSetupViewModel { ConnectionWorking: true } vm)
                    {
                        await InitializeMainWindow(vm.SelectedDevice.Path, vm.SelectedBaudRate, desktop);
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
                await InitializeMainWindow(null, 0, desktop);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task InitializeMainWindow(string port, int baudRate, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var splashScreen = new SplashScreen();
        desktop.MainWindow = splashScreen;
        splashScreen.Show();

        try
        {
            var viewModel = await CreateMainWindowViewModel(port, baudRate);
            OnViewModelCreated(viewModel, splashScreen, desktop);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fehler: {ex.Message}");
            desktop.Shutdown();
        }
    }

    private void OnViewModelCreated(MainWindowViewModel viewModel, SplashScreen splashScreen,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        // UI-Thread verwenden, um Änderungen an der UI vorzunehmen
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            var mainWindow = new MainWindow
            {
                DataContext = viewModel
            };

            desktop.MainWindow = mainWindow;
            mainWindow.Show();
            splashScreen.Close();
        });
    }

    private async Task<MainWindowViewModel> CreateMainWindowViewModel(string port = null, int baudrate = 0)
    {
        var collection = new ServiceCollection();
        collection.AddCommonServices();

        var services = collection.BuildServiceProvider();

        services.PostInit();

        var mainViewModel = services.GetRequiredService<MainWindowViewModel>();

        await mainViewModel.LoupedeckController.Initialize(port, baudrate);

        return mainViewModel;
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}