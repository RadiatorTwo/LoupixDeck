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
                var initWindow = new InitSetup();
                var tcs = new TaskCompletionSource<bool>();

                initWindow.DataContext = new InitSetupViewModel();

                initWindow.Closed += (_, _) => { tcs.TrySetResult(true); };

                desktop.MainWindow = initWindow;
                initWindow.Show();

                await tcs.Task;

                if (initWindow.DataContext is InitSetupViewModel { ConnectionWorking: true } vm)
                {
                    var splashScreen = new SplashScreen();
                    desktop.MainWindow = splashScreen;
                    splashScreen.Show();
                    
#pragma warning disable CS4014
                    Task.Run(async () =>
#pragma warning restore CS4014
                    {
                        var viewModel = await CreateMainWindowViewModel(vm.SelectedDevice.Path, vm.SelectedBaudRate);
                        OnViewModelCreated(viewModel, splashScreen, desktop);
                    }).ContinueWith(t =>
                    {
                        if (t.Exception == null) return;
                        
                        // Fehlerbehandlung hier
                        foreach (var ex in t.Exception.Flatten().InnerExceptions)
                        {
                            Console.WriteLine($"Fehler im Task: {ex}");
                        }
                    }, TaskContinuationOptions.OnlyOnFaulted);
                }
            }
            else
            {
                var splashScreen = new SplashScreen();
                desktop.MainWindow = splashScreen;
                splashScreen.Show();
                
#pragma warning disable CS4014
                Task.Run(async () =>
#pragma warning restore CS4014
                {
                    var viewModel = await CreateMainWindowViewModel();
                    OnViewModelCreated(viewModel, splashScreen, desktop);
                }).ContinueWith(t =>
                {
                    if (t.Exception == null) return;
                    
                    // Fehlerbehandlung hier
                    foreach (var ex in t.Exception.Flatten().InnerExceptions)
                    {
                        Console.WriteLine($"Fehler im Task: {ex}");
                    }
                }, TaskContinuationOptions.OnlyOnFaulted);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
    
    private void OnViewModelCreated(MainWindowViewModel viewModel, SplashScreen splashScreen, IClassicDesktopStyleApplicationLifetime desktop)
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