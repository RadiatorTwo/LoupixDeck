using LoupixDeck.Controllers;
using LoupixDeck.Models;
using LoupixDeck.Services;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels;
using LoupixDeck.Views;
using Microsoft.Extensions.DependencyInjection;

namespace LoupixDeck;

public static class ServiceCollectionExtensions
{
    public static void AddCommonServices(this IServiceCollection collection)
    {
        collection.AddSingleton(provider =>
        {
            var configService = provider.GetRequiredService<IConfigService>();
            var configPath = FileDialogHelper.GetConfigPath("config.json");
            var config = configService.LoadConfig<LoupedeckConfig>(configPath);
            return config ?? new LoupedeckConfig();
        });

        collection.AddSingleton<IConfigService, ConfigService>();
        collection.AddSingleton<ICommandService, CommandService>();
        collection.AddSingleton<IDeviceService, LoupedeckDeviceService>();
        collection.AddSingleton<IPageManager, PageManager>();

        var elgatoDevices = ElgatoDevices.LoadFromFile();

        if (elgatoDevices != null)
        {
            collection.AddSingleton(elgatoDevices);
        }
        else
        {
            collection.AddSingleton<ElgatoDevices>();
        }

        collection.AddSingleton<ICommandBuilder, CommandBuilder>();
        collection.AddSingleton<ISysCommandService, SysCommandService>();
        collection.AddSingleton<IUInputKeyboard, UInputKeyboard>();

        collection.AddSingleton<IObsController, ObsController>();
        collection.AddSingleton<IDBusController, DBusController>();
        collection.AddSingleton<ICommandRunner, CommandRunner>();
        collection.AddSingleton<IElgatoController, ElgatoController>();

        collection.AddSingleton<LoupedeckLiveSController>();

        collection.AddTransient<MainWindowViewModel>();

        InitDialogs(collection);
    }

    private static void InitDialogs(IServiceCollection collection)
    {
        collection.AddTransient<SimpleButtonSettings>();
        collection.AddTransient<SimpleButtonSettingsViewModel>();

        collection.AddTransient<RotaryButtonSettings>();
        collection.AddTransient<RotaryButtonSettingsViewModel>();

        collection.AddTransient<TouchButtonSettings>();
        collection.AddTransient<TouchButtonSettingsViewModel>();
        
        collection.AddTransient<Settings>();
        collection.AddTransient<SettingsViewModel>();
        
        collection.AddSingleton<IDialogService, DialogService>();
    }

    public static void PostInit(this IServiceProvider services)
    {
        var dialogService = services.GetRequiredService<IDialogService>();

        dialogService.Register<SimpleButtonSettingsViewModel, SimpleButtonSettings>();
        dialogService.Register<RotaryButtonSettingsViewModel, RotaryButtonSettings>();
        dialogService.Register<TouchButtonSettingsViewModel, TouchButtonSettings>();
        dialogService.Register<SettingsViewModel, Settings>();
    }
}