using LoupixDeck.Controllers;
using LoupixDeck.Models;
using LoupixDeck.Registry;
using LoupixDeck.Services;
using LoupixDeck.Services.Commands;
using LoupixDeck.Services.FolderNavigation;
using LoupixDeck.Services.Plugins;
using LoupixDeck.Services.SystemPower;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels;
using LoupixDeck.Views;
using Microsoft.Extensions.DependencyInjection;

namespace LoupixDeck;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Wires every service. <paramref name="deviceInfo"/> selects the device
    /// whose per-device config file will be loaded; when null we fall back to
    /// the legacy "config.json" path so an existing single-device install
    /// boots even before the resolver has run (defensive — App resolves first
    /// in normal flow).
    /// </summary>
    public static void AddCommonServices(this IServiceCollection collection, DeviceRegistry.DeviceInfo deviceInfo)
    {
        ArgumentNullException.ThrowIfNull(deviceInfo);
        collection.AddSingleton(deviceInfo);

        collection.AddSingleton(provider =>
        {
            var configService = provider.GetRequiredService<IConfigService>();
            var configPath = FileDialogHelper.GetConfigPath(deviceInfo);
            var config = configService.LoadConfig<LoupedeckConfig>(configPath);
            if (config == null)
            {
                config = new LoupedeckConfig
                {
                    DeviceVid = deviceInfo.VendorId,
                    DevicePid = deviceInfo.ProductId
                };

                // First launch for this device — seed the serial port/baud from any
                // existing sibling config so the user does not have to re-run InitSetup
                // just because they switched device type (the port is hardware, not
                // device-type-specific). Crucial for the LOUPIXDECK_FAKE_DEVICE flow:
                // without this the fresh config has no port → device times out →
                // App.CreateMainWindowViewModel catches and shuts down silently.
                SeedSerialPortFromSibling(config, configService, deviceInfo);
            }
            return config;
        });

        collection.AddSingleton<IConfigService, ConfigService>();
        collection.AddSingleton<IAssetService, AssetService>();
        collection.AddSingleton<ICommandService, CommandService>();
        collection.AddSingleton<IDeviceService, LoupedeckDeviceService>();
        collection.AddSingleton<IPageManager, PageManager>();

        collection.AddSingleton<ICommandBuilder, CommandBuilder>();
        collection.AddSingleton<ISysCommandService, SysCommandService>();

        // The command registry unifies built-in commands and plugin commands.
        // CoreCommandProvider wraps the reflection-based scanner;
        // PluginCommandProvider feeds in commands from loaded plugins.
        collection.AddSingleton<IPluginManager, PluginManager>();
        collection.AddSingleton<ICommandProvider, CoreCommandProvider>();
        collection.AddSingleton<ICommandProvider, PluginCommandProvider>();
        collection.AddSingleton<ICommandRegistry, CommandRegistry>();

        // The command-selection menu is assembled generically from these
        // contributors instead of the former per-ViewModel hard-coded logic.
        collection.AddSingleton<IMenuContributor, CommandGroupMenuContributor>();
        collection.AddSingleton<IPluginMenuSource, PluginMenuContributor>();
        collection.AddSingleton<IMenuTreeBuilder, MenuTreeBuilder>();

        // UInputKeyboard is only available on Linux
        if (OperatingSystem.IsLinux())
        {
            collection.AddSingleton<IUInputKeyboard, UInputKeyboard>();
        }
        else
        {
            collection.AddSingleton<IUInputKeyboard, WindowsUInputKeyboard>();
        }

        collection.AddSingleton<IDBusController, DBusController>();
        collection.AddSingleton<ICommandRunner, CommandRunner>();
        collection.AddSingleton<IDynamicTextManager, DynamicTextManager>();
        collection.AddSingleton<IFolderNavigationService, FolderNavigationService>();
        collection.AddSingleton<IExclusiveModeService, ExclusiveModeService>();

        collection.AddSingleton<INativeHapticService, NativeHapticService>();

        if (OperatingSystem.IsLinux())
            collection.AddSingleton<ISystemPowerService, LinuxSystemPowerService>();
#if WINDOWS
        else if (OperatingSystem.IsWindows())
            collection.AddSingleton<ISystemPowerService, WindowsSystemPowerService>();
#endif
        else
            collection.AddSingleton<ISystemPowerService, NoOpSystemPowerService>();

        collection.AddSingleton<LoupedeckLiveSController>();
        collection.AddSingleton<IDeviceController>(sp => sp.GetRequiredService<LoupedeckLiveSController>());

        collection.AddTransient<MainWindowViewModel>();

        InitDialogs(collection);
    }

    private static void SeedSerialPortFromSibling(LoupedeckConfig fresh, IConfigService configService,
        DeviceRegistry.DeviceInfo self)
    {
        try
        {
            var dir = FileDialogHelper.GetConfigDir();
            var candidates = DeviceRegistry.SupportedDevices
                .Where(d => d.Slug != self.Slug)
                .Select(d => FileDialogHelper.GetConfigPath(d))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();

            foreach (var path in candidates)
            {
                var sibling = configService.LoadConfig<LoupedeckConfig>(path);
                if (sibling == null || string.IsNullOrEmpty(sibling.DevicePort)) continue;
                fresh.DevicePort = sibling.DevicePort;
                fresh.DeviceBaudrate = sibling.DeviceBaudrate;
                Console.WriteLine($"[Config] Seeded {self.Slug} port from sibling: {sibling.DevicePort} @ {sibling.DeviceBaudrate}");
                return;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config] Sibling-port seed failed: {ex.Message}");
        }
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

        collection.AddTransient<TouchPageWallpaperSettings>();
        collection.AddTransient<TouchPageWallpaperSettingsViewModel>();

        collection.AddTransient<PageCommandsSettings>();
        collection.AddTransient<PageCommandsSettingsViewModel>();

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
        dialogService.Register<TouchPageWallpaperSettingsViewModel, TouchPageWallpaperSettings>();
        dialogService.Register<PageCommandsSettingsViewModel, PageCommandsSettings>();
        dialogService.Register<SettingsViewModel, Settings>();
        dialogService.Register<AboutViewModel, About>();

        // Let the (static) bitmap renderer resolve image-layer assets via DI.
        var assetService = services.GetRequiredService<IAssetService>();
        BitmapHelper.AssetResolver = assetService.Load;

        // Heal configs that were saved before HapticSteps had ObjectCreationHandling.Replace —
        // those files accumulated duplicate steps on every save+load round.
        var hapticConfig = services.GetRequiredService<LoupedeckConfig>();
        while (hapticConfig.HapticSteps.Count > SettingsViewModel.MaxHapticSteps)
            hapticConfig.HapticSteps.RemoveAt(hapticConfig.HapticSteps.Count - 1);
        if (hapticConfig.HapticSteps.Count == 0)
            hapticConfig.HapticSteps.Add(new HapticStep());

        // Materialize the haptic service so it subscribes to config/page events,
        // and push the persisted config to the device once it's connected.
        services.GetRequiredService<INativeHapticService>().Apply();

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