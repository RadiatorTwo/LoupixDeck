using System.Reflection;
using LoupixDeck.PluginSdk;
using LoupixDeck.Registry;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using SdkDeviceInfo = LoupixDeck.PluginSdk.DeviceInfo;

namespace LoupixDeck.Services.Plugins;

/// <summary>
/// Discovers, loads and initializes plugins from the <c>plugins/</c> directory
/// next to the application. Each plugin is isolated in its own collectible
/// <see cref="PluginLoadContext"/>; a failure in one plugin never prevents the
/// app — or the other plugins — from starting.
/// </summary>
public interface IPluginManager
{
    /// <summary>All discovered plugins, including failed/incompatible ones.</summary>
    IReadOnlyList<LoadedPlugin> Plugins { get; }

    /// <summary>Scans the plugins directory and loads every discovered plugin.</summary>
    void LoadPlugins();

    /// <summary>Shuts down every loaded plugin and unloads its context.</summary>
    void ShutdownAll();
}

/// <inheritdoc cref="IPluginManager"/>
public class PluginManager : IPluginManager
{
    private readonly IServiceProvider _serviceProvider;
    private readonly DeviceRegistry.DeviceInfo _deviceInfo;
    private readonly Models.LoupedeckConfig _config;
    private readonly List<LoadedPlugin> _plugins = new();

    public PluginManager(IServiceProvider serviceProvider, DeviceRegistry.DeviceInfo deviceInfo,
        Models.LoupedeckConfig config)
    {
        _serviceProvider = serviceProvider;
        _deviceInfo = deviceInfo;
        _config = config;
    }

    public IReadOnlyList<LoadedPlugin> Plugins => _plugins;

    public void LoadPlugins()
    {
        _plugins.Clear();

        var pluginsRoot = Path.Combine(AppContext.BaseDirectory, "plugins");
        if (!Directory.Exists(pluginsRoot))
        {
            Console.WriteLine($"PluginManager: no plugins directory at '{pluginsRoot}' — core only.");
            return;
        }

        foreach (var dir in Directory.GetDirectories(pluginsRoot))
        {
            var manifestPath = Path.Combine(dir, "plugin.json");
            if (!File.Exists(manifestPath))
                continue;

            _plugins.Add(LoadOne(dir, manifestPath));
        }

        var ok = _plugins.Count(p => p.Status == PluginLoadStatus.Loaded);
        Console.WriteLine($"PluginManager: {ok}/{_plugins.Count} plugin(s) loaded.");
    }

    private LoadedPlugin LoadOne(string dir, string manifestPath)
    {
        PluginManifest manifest = null;
        try
        {
            manifest = JsonConvert.DeserializeObject<PluginManifest>(File.ReadAllText(manifestPath));
        }
        catch (Exception ex)
        {
            return Fail(dir, null, $"Invalid plugin.json: {ex.Message}");
        }

        if (manifest == null || string.IsNullOrWhiteSpace(manifest.Id)
            || string.IsNullOrWhiteSpace(manifest.EntryAssembly))
        {
            return Fail(dir, manifest, "plugin.json is missing 'id' or 'entryAssembly'.");
        }

        // User gate — a plugin only loads when enabled in Settings → Plugins.
        if (!IsEnabled(manifest.Id))
        {
            return new LoadedPlugin
            {
                Manifest = manifest,
                Directory = dir,
                Status = PluginLoadStatus.Disabled,
                FailureReason = "Disabled — enable it in Settings → Plugins (requires a restart)."
            };
        }

        // Platform gate — skip plugins not meant for this OS.
        if (!PlatformMatches(manifest.Platform))
        {
            return new LoadedPlugin
            {
                Manifest = manifest,
                Directory = dir,
                Status = PluginLoadStatus.Disabled,
                FailureReason = $"Plugin targets '{manifest.Platform}', not this OS."
            };
        }

        // SDK compatibility — the major version must match the host SDK.
        if (!Version.TryParse(manifest.SdkVersion, out var pluginSdk))
        {
            return Incompatible(dir, manifest, $"Unparseable sdkVersion '{manifest.SdkVersion}'.");
        }

        if (pluginSdk.Major != SdkInfo.Version.Major)
        {
            return Incompatible(dir, manifest,
                $"Plugin SDK {pluginSdk} is incompatible with host SDK {SdkInfo.Version}.");
        }

        var entryPath = Path.Combine(dir, manifest.EntryAssembly);
        if (!File.Exists(entryPath))
        {
            return Fail(dir, manifest, $"Entry assembly '{manifest.EntryAssembly}' not found.");
        }

        PluginLoadContext context = null;
        try
        {
            context = new PluginLoadContext(entryPath);
            var assembly = context.LoadFromAssemblyPath(entryPath);

            var pluginType = assembly.GetTypes()
                .FirstOrDefault(t => !t.IsAbstract && typeof(LoupixPlugin).IsAssignableFrom(t));

            if (pluginType == null)
            {
                context.Unload();
                return Fail(dir, manifest, "No LoupixPlugin implementation found in entry assembly.");
            }

            var instance = (LoupixPlugin)Activator.CreateInstance(pluginType);

            var host = CreateHost(manifest, dir);
            instance.Initialize(host);

            var commands = instance.GetCommands()?.Where(c => c != null).ToList()
                           ?? new List<IPluginCommand>();

            return new LoadedPlugin
            {
                Manifest = manifest,
                Directory = dir,
                Status = PluginLoadStatus.Loaded,
                Instance = instance,
                LoadContext = context,
                Host = host,
                Commands = commands
            };
        }
        catch (Exception ex)
        {
            try { context?.Unload(); } catch { /* best effort */ }
            return Fail(dir, manifest, $"Load/initialize threw: {ex.Message}");
        }
    }

    private PluginHost CreateHost(PluginManifest manifest, string dir)
    {
        var logger = new PluginLogger(manifest.Id);
        var settings = new PluginSettingsStore(Path.Combine(dir, "settings.json"));
        var device = new SdkDeviceInfo(
            _deviceInfo.Name, _deviceInfo.VendorId, _deviceInfo.ProductId, _deviceInfo.Slug);

        // Resolved lazily at call time so host operations work regardless of
        // service construction order.
        void ExecuteCommand(string command)
        {
            try
            {
                // Chained from a plugin — there's no triggering button.
                _ = _serviceProvider.GetRequiredService<ICommandService>().ExecuteCommand(command, ButtonTargets.None);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PluginHost[{manifest.Id}]: ExecuteCommand failed: {ex.Message}");
            }
        }

        void RequestButtonRefresh(string commandName)
        {
            try
            {
                _serviceProvider.GetRequiredService<IDynamicTextManager>().RefreshCommand(commandName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PluginHost[{manifest.Id}]: RequestButtonRefresh failed: {ex.Message}");
            }
        }

        void OpenFolder(IFolderProvider provider)
        {
            try
            {
                var nav = _serviceProvider.GetRequiredService<FolderNavigation.IFolderNavigationService>();
                _ = nav.OpenFolder(new PluginFolderAdapter(provider));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PluginHost[{manifest.Id}]: OpenFolder failed: {ex.Message}");
            }
        }

        void OverlayTouchText(int slot, string text, TimeSpan duration)
        {
            try
            {
                var devSvc = _serviceProvider.GetRequiredService<IDeviceService>();
                // Fire and forget — the host's ShowTemporaryTextButton already
                // self-supersedes via its internal call-ID counter, so quick
                // repeated invocations don't queue up restore-races.
                _ = devSvc.ShowTemporaryTextButton(slot, text ?? string.Empty,
                    (int)Math.Max(50, duration.TotalMilliseconds));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PluginHost[{manifest.Id}]: OverlayTouchText failed: {ex.Message}");
            }
        }

        int GetTouchSlotForRotary(int rotaryIndex)
        {
            try
            {
                return _serviceProvider.GetRequiredService<IDeviceService>().Device?
                    .GetTouchSlotForRotary(rotaryIndex) ?? -1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PluginHost[{manifest.Id}]: GetTouchSlotForRotary failed: {ex.Message}");
                return -1;
            }
        }

        bool RequestExclusiveMode(IExclusiveModeProvider provider)
        {
            try
            {
                return _serviceProvider.GetRequiredService<IExclusiveModeService>().TryEnter(provider);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PluginHost[{manifest.Id}]: RequestExclusiveMode failed: {ex.Message}");
                return false;
            }
        }

        void ReleaseExclusiveMode(IExclusiveModeProvider provider)
        {
            try
            {
                _serviceProvider.GetRequiredService<IExclusiveModeService>().Exit(provider);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PluginHost[{manifest.Id}]: ReleaseExclusiveMode failed: {ex.Message}");
            }
        }

        bool IsInExclusiveMode()
        {
            try { return _serviceProvider.GetRequiredService<IExclusiveModeService>().IsActive; }
            catch { return false; }
        }

        return new PluginHost(logger, settings, device, ExecuteCommand, RequestButtonRefresh,
            OpenFolder, OverlayTouchText, GetTouchSlotForRotary,
            RequestExclusiveMode, ReleaseExclusiveMode, IsInExclusiveMode);
    }

    public void ShutdownAll()
    {
        foreach (var plugin in _plugins.Where(p => p.Status == PluginLoadStatus.Loaded))
        {
            try
            {
                plugin.Instance?.Shutdown();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PluginManager: '{plugin.Manifest?.Id}' Shutdown threw: {ex.Message}");
            }

            try
            {
                plugin.LoadContext?.Unload();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PluginManager: '{plugin.Manifest?.Id}' Unload threw: {ex.Message}");
            }
        }
    }

    private bool IsEnabled(string pluginId)
    {
        return _config.EnabledPlugins != null
               && _config.EnabledPlugins.Any(id => string.Equals(id, pluginId, StringComparison.OrdinalIgnoreCase));
    }

    private static bool PlatformMatches(string platform)
    {
        if (string.IsNullOrWhiteSpace(platform) ||
            platform.Equals("All", StringComparison.OrdinalIgnoreCase))
            return true;

        if (platform.Equals("Windows", StringComparison.OrdinalIgnoreCase))
            return OperatingSystem.IsWindows();

        if (platform.Equals("Linux", StringComparison.OrdinalIgnoreCase))
            return OperatingSystem.IsLinux();

        return false;
    }

    private static LoadedPlugin Fail(string dir, PluginManifest manifest, string reason)
    {
        Console.WriteLine($"PluginManager: '{manifest?.Id ?? dir}' failed — {reason}");
        return new LoadedPlugin
        {
            Manifest = manifest,
            Directory = dir,
            Status = PluginLoadStatus.Failed,
            FailureReason = reason
        };
    }

    private static LoadedPlugin Incompatible(string dir, PluginManifest manifest, string reason)
    {
        Console.WriteLine($"PluginManager: '{manifest?.Id ?? dir}' incompatible — {reason}");
        return new LoadedPlugin
        {
            Manifest = manifest,
            Directory = dir,
            Status = PluginLoadStatus.Incompatible,
            FailureReason = reason
        };
    }
}
