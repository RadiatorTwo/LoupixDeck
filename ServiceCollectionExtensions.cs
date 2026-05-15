using LoupixDeck.Controllers;
using LoupixDeck.Models;
using LoupixDeck.Services;
using LoupixDeck.Services.Argus;
using LoupixDeck.Services.Audio;
using LoupixDeck.Services.FolderNavigation;
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
        collection.AddSingleton<IAssetService, AssetService>();
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

        // UInputKeyboard is only available on Linux
        if (OperatingSystem.IsLinux())
        {
            collection.AddSingleton<IUInputKeyboard, UInputKeyboard>();
        }
        else
        {
            collection.AddSingleton<IUInputKeyboard, WindowsUInputKeyboard>();
        }

        collection.AddSingleton<IObsController, ObsController>();
        collection.AddSingleton<IDBusController, DBusController>();
        collection.AddSingleton<ICommandRunner, CommandRunner>();
        collection.AddSingleton<IElgatoController, ElgatoController>();
        collection.AddSingleton<ICoolerControlApiController, CoolerControlApiController>();
        collection.AddSingleton<IArgusMonitorService, ArgusMonitorService>();
        collection.AddSingleton<IDynamicTextManager, DynamicTextManager>();
        collection.AddSingleton<IFolderNavigationService, FolderNavigationService>();

        if (OperatingSystem.IsWindows())
        {
            collection.AddSingleton<IWindowsAudioService, WindowsAudioService>();
        }
        else
        {
            collection.AddSingleton<IWindowsAudioService, NoOpAudioService>();
        }

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

        collection.AddTransient<SymbolPicker>();
        collection.AddTransient<SymbolPickerViewModel>();

        collection.AddTransient<Settings>();
        collection.AddTransient<SettingsViewModel>();
        
        collection.AddTransient<About>();
        collection.AddTransient<AboutViewModel>();
        
        collection.AddSingleton<IDialogService, DialogService>();
    }

    public static void PostInit(this IServiceProvider services)
    {
        var dialogService = services.GetRequiredService<IDialogService>();

        dialogService.Register<SimpleButtonSettingsViewModel, SimpleButtonSettings>();
        dialogService.Register<RotaryButtonSettingsViewModel, RotaryButtonSettings>();
        dialogService.Register<TouchButtonSettingsViewModel, TouchButtonSettings>();
        dialogService.Register<SymbolPickerViewModel, SymbolPicker>();
        dialogService.Register<SettingsViewModel, Settings>();
        dialogService.Register<AboutViewModel, About>();

        // Let the (static) bitmap renderer resolve image-layer assets via DI.
        var assetService = services.GetRequiredService<IAssetService>();
        BitmapHelper.AssetResolver = assetService.Load;

        // After config load, rewire per-layer PropertyChanged handlers so edits
        // trigger TouchButton.Refresh(). The collection setter in TouchButton
        // wires its own CollectionChanged hook, but layers created by the JSON
        // converter bypass AttachLayerHandlers.
        var config = services.GetRequiredService<LoupedeckConfig>();
        if (config.TouchButtonPages != null)
        {
            foreach (var page in config.TouchButtonPages)
            {
                if (page?.TouchButtons == null) continue;
                foreach (var button in page.TouchButtons)
                {
                    button?.RewireLayerHandlers();
                }
            }
        }
    }
}