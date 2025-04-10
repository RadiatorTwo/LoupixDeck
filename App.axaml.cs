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
using AutoMapper;

namespace LoupixDeck;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
        // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
        DisableAvaloniaDataAnnotationValidation();
        
        // Register all the services needed for the application to run
        var collection = new ServiceCollection();
        collection.AddCommonServices();

        // Creates a ServiceProvider containing services from the provided IServiceCollection
        var services = collection.BuildServiceProvider();
        
        var loupeDeckDevice = LoadFromFile<LoupedeckLiveS>(services);

        if (loupeDeckDevice == null)
        {
            collection.AddSingleton<LoupedeckLiveS>();
        }
        else
        {
            collection.AddSingleton(loupeDeckDevice);
        }

        var vm = services.GetRequiredService<MainWindowViewModel>();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = vm
            };
        }

        base.OnFrameworkInitializationCompleted();
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