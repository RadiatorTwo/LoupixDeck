using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using LoupixDeck.LoupedeckDevice.Device;
using LoupixDeck.Views;
using LoupixDeck.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using LoupixDeck.Models;
using LoupixDeck.Models.Converter;
using LoupixDeck.Utils;
using Newtonsoft.Json;

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
                    var mainViewModel = CreateMainWindowViewModel(vm.SelectedDevice, vm.SelectedBaudRate);

                    var mainWindow = new MainWindow
                    {
                        DataContext = mainViewModel
                    };

                    desktop.MainWindow = mainWindow;
                    mainWindow.Show();
                }
            }
            else
            {
                var viewModel = CreateMainWindowViewModel();
                desktop.MainWindow = new MainWindow
                {
                    DataContext = viewModel
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private MainWindowViewModel CreateMainWindowViewModel(string port = null, int baudrate = 0)
    {
        var collection = new ServiceCollection();
        collection.AddCommonServices();

        var services = collection.BuildServiceProvider();

        services.PostInit();

        var mainViewModel = services.GetRequiredService<MainWindowViewModel>();

        mainViewModel.LoupedeckController.Initialize(port, baudrate);

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