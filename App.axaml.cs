using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using LoupixDeck.Commands.Base;
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
                
                initWindow.Closed += (_, _) =>
                {
                    tcs.TrySetResult(true);
                };
                
                desktop.MainWindow = initWindow;
                initWindow.Show();

                await tcs.Task;
                
                var mainViewModel = CreateMainWindowViewModel();
                var mainWindow = new MainWindow
                {
                    DataContext = mainViewModel
                };

                desktop.MainWindow = mainWindow;
                mainWindow.Show();
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

    private MainWindowViewModel CreateMainWindowViewModel()
    {
        var collection = new ServiceCollection();
        collection.AddCommonServices();

        var services = collection.BuildServiceProvider();

        CommandManager.Initialize(services);

        var loupeDeckDevice = LoadFromFile<LoupedeckLiveS>(services);

        if (loupeDeckDevice == null)
        {
            collection.AddSingleton<LoupedeckLiveS>();
        }
        else
        {
            collection.AddSingleton(loupeDeckDevice);
        }

        var mainViewModel = services.GetRequiredService<MainWindowViewModel>();
        
        return mainViewModel;
    }

    private static T LoadFromFile<T>(IServiceProvider provider) where T : LoupedeckBase
    {
        var filePath = FileDialogHelper.GetConfigPath("config.json");

        if (!File.Exists(filePath))
            return null;

        var json = File.ReadAllText(filePath);

        var settings = new JsonSerializerSettings();
        settings.Converters.Add(new LoupedeckConverter(provider));
        settings.Converters.Add(new ColorJsonConverter());
        settings.Converters.Add(new BitmapJsonConverter());

        var instance = JsonConvert.DeserializeObject<T>(json, settings);
        instance.CurrentTouchPageIndex = 0;
        instance.CurrentRotaryPageIndex = 0;

        return instance;
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