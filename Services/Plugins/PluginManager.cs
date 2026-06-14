using System.Reflection;
using LoupixDeck.PluginSdk;
using LoupixDeck.Registry;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using SdkDeviceInfo = LoupixDeck.PluginSdk.DeviceInfo;

namespace LoupixDeck.Services.Plugins;

/// <summary>
/// Discovers, loads and initializes plugins from the bundled <c>plugins/</c>
/// directory next to the application and from the user <c>plugins/</c> directory
/// in the config folder (<c>~/.config/LoupixDeck/plugins</c>). Each plugin is
/// isolated in its own collectible
/// <see cref="PluginLoadContext"/>; a failure in one plugin never prevents the
/// app — or the other plugins — from starting.
/// </summary>
public interface IPluginManager
{
    /// <summary>All discovered plugins, including failed/incompatible ones.</summary>
    IReadOnlyList<LoadedPlugin> Plugins { get; }

    /// <summary>Scans the plugins directory and loads every discovered plugin.</summary>
    void LoadPlugins();

    /// <summary>
    /// Loads (or reloads) a single plugin by id at runtime, replacing any existing
    /// entry with the same id. Honours the same enable/platform/SDK gates as the
    /// bulk load, so a disabled plugin yields a <see cref="PluginLoadStatus.Disabled"/>
    /// entry without creating a load context. UI thread only. Returns the resulting
    /// <see cref="LoadedPlugin"/>, or null when no manifest is found for the id.
    /// </summary>
    LoadedPlugin LoadPlugin(string pluginId);

    /// <summary>
    /// Shuts down and unloads a single plugin by id, dropping it from
    /// <see cref="Plugins"/>. The collectible context unload is best-effort — actual
    /// collection (and file-lock release) is not guaranteed. UI thread only.
    /// Returns true when an unload was requested.
    /// </summary>
    bool UnloadPlugin(string pluginId);

    /// <summary>Shuts down every loaded plugin and unloads its context.</summary>
    void ShutdownAll();
}

/// <inheritdoc cref="IPluginManager"/>
public class PluginManager : IPluginManager
{
    private readonly IServiceProvider _serviceProvider;
    private readonly DeviceRegistry.DeviceInfo _deviceInfo;
    private readonly Models.LoupedeckConfig _config;

    // Copy-on-write snapshot. Every mutation builds a new list and swaps this
    // reference, so readers (e.g. PluginCommandProvider during a registry rebuild)
    // always see a consistent, immutable list — never a torn mid-mutation state.
    private volatile IReadOnlyList<LoadedPlugin> _plugins = Array.Empty<LoadedPlugin>();

    public PluginManager(IServiceProvider serviceProvider, DeviceRegistry.DeviceInfo deviceInfo,
        Models.LoupedeckConfig config)
    {
        _serviceProvider = serviceProvider;
        _deviceInfo = deviceInfo;
        _config = config;
    }

    public IReadOnlyList<LoadedPlugin> Plugins => _plugins;

    /// <summary>Builds a new plugin list from the current snapshot and publishes it.</summary>
    private void ReplacePlugins(Action<List<LoadedPlugin>> mutate)
    {
        var next = new List<LoadedPlugin>(_plugins);
        mutate(next);
        _plugins = next; // atomic reference swap
    }

    /// <summary>The two discovery roots, app dir first so bundled plugins win.</summary>
    private static string[] GetPluginRoots()
    {
        var userPluginsRoot = Path.Combine(Utils.FileDialogHelper.GetConfigDir(), "plugins");
        return
        [
            Path.Combine(AppContext.BaseDirectory, "plugins"),
            userPluginsRoot
        ];
    }

    /// <summary>
    /// Finds the directory + manifest path for a plugin id across both roots
    /// (app dir wins). Returns false when no manifest with that id exists.
    /// </summary>
    private static bool TryResolvePluginDir(string pluginId, out string dir, out string manifestPath)
    {
        dir = null;
        manifestPath = null;
        if (string.IsNullOrWhiteSpace(pluginId))
            return false;

        foreach (var root in GetPluginRoots())
        {
            if (!Directory.Exists(root))
                continue;

            foreach (var candidate in Directory.GetDirectories(root))
            {
                var candidateManifest = Path.Combine(candidate, "plugin.json");
                if (!File.Exists(candidateManifest))
                    continue;

                string id;
                try
                {
                    id = JsonConvert.DeserializeObject<PluginManifest>(File.ReadAllText(candidateManifest))?.Id;
                }
                catch
                {
                    continue;
                }

                if (string.Equals(id, pluginId, StringComparison.OrdinalIgnoreCase))
                {
                    dir = candidate;
                    manifestPath = candidateManifest;
                    return true;
                }
            }
        }

        return false;
    }

    public void LoadPlugins()
    {
        var loadedPlugins = new List<LoadedPlugin>();

        // Plugins are discovered from two roots: the bundled `plugins/` folder
        // next to the executable, and a user `plugins/` folder alongside the
        // config files (~/.config/LoupixDeck/plugins). The app directory is
        // scanned first so bundled plugins win when an id appears in both.
        // The user plugins folder is created on startup so it always exists for
        // the user to drop plugins into (and for "Open Plugins Folder" to open).
        var userPluginsRoot = Path.Combine(Utils.FileDialogHelper.GetConfigDir(), "plugins");
        try
        {
            Directory.CreateDirectory(userPluginsRoot);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"PluginManager: could not create user plugins dir '{userPluginsRoot}': {ex.Message}");
        }

        // Carry out any lifecycle ops that were deferred while assemblies were
        // locked — now, before anything is loaded. Installs (staged update swaps)
        // first, then removals.
        PluginInstaller.ProcessPendingInstalls(userPluginsRoot);
        PluginInstaller.ProcessPendingRemovals(userPluginsRoot);

        var pluginsRoots = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "plugins"),
            userPluginsRoot
        };

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pluginsRoot in pluginsRoots)
        {
            if (!Directory.Exists(pluginsRoot))
            {
                Console.WriteLine($"PluginManager: no plugins directory at '{pluginsRoot}'.");
                continue;
            }

            foreach (var dir in Directory.GetDirectories(pluginsRoot))
            {
                var manifestPath = Path.Combine(dir, "plugin.json");
                if (!File.Exists(manifestPath))
                    continue;

                var loaded = LoadOne(dir, manifestPath);

                // Skip a plugin whose id was already discovered in an earlier
                // root, so a user copy can't shadow/collide with a bundled one.
                var id = loaded.Manifest?.Id;
                if (!string.IsNullOrWhiteSpace(id) && !seenIds.Add(id))
                {
                    Console.WriteLine(
                        $"PluginManager: skipping duplicate plugin id '{id}' at '{dir}' " +
                        "(already loaded from an earlier directory).");
                    continue;
                }

                loadedPlugins.Add(loaded);
            }
        }

        _plugins = loadedPlugins; // single atomic publish

        var ok = loadedPlugins.Count(p => p.Status == PluginLoadStatus.Loaded);
        Console.WriteLine($"PluginManager: {ok}/{loadedPlugins.Count} plugin(s) loaded.");
    }

    public LoadedPlugin LoadPlugin(string pluginId)
    {
        if (!TryResolvePluginDir(pluginId, out var dir, out var manifestPath))
        {
            Console.WriteLine($"PluginManager: cannot load '{pluginId}' — no manifest found.");
            return null;
        }

        // Replace any prior entry for this id, then load fresh. LoadOne applies the
        // enable/platform/SDK gates, so a disabled plugin produces a Disabled entry
        // with no load context (nothing to unload, no file lock).
        UnloadPlugin(pluginId);

        var loaded = LoadOne(dir, manifestPath);
        ReplacePlugins(list =>
        {
            list.RemoveAll(p => string.Equals(p.Manifest?.Id, pluginId, StringComparison.OrdinalIgnoreCase));
            list.Add(loaded);
        });

        return loaded;
    }

    public bool UnloadPlugin(string pluginId)
    {
        var plugin = _plugins.FirstOrDefault(
            p => string.Equals(p.Manifest?.Id, pluginId, StringComparison.OrdinalIgnoreCase));
        if (plugin == null)
            return false;

        if (plugin.Status == PluginLoadStatus.Loaded)
        {
            try { plugin.Instance?.Shutdown(); }
            catch (Exception ex) { Console.WriteLine($"PluginManager: '{pluginId}' Shutdown threw: {ex.Message}"); }
        }

        ReplacePlugins(list =>
            list.RemoveAll(p => string.Equals(p.Manifest?.Id, pluginId, StringComparison.OrdinalIgnoreCase)));

        // Drop every strong reference we own so the only roots left are external
        // (in-flight Execute, etc.), then request the collectible unload.
        var context = plugin.LoadContext;
        plugin.Instance = null;
        plugin.Host = null;
        plugin.Commands = Array.Empty<IPluginCommand>();
        plugin.SideStripProviders = Array.Empty<ISideStripProvider>();
        plugin.LoadContext = null;

        try { context?.Unload(); }
        catch (Exception ex) { Console.WriteLine($"PluginManager: '{pluginId}' Unload threw: {ex.Message}"); }

        return context != null;
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

            var stripProviders = instance.GetSideStripProviders()?.Where(p => p != null).ToList()
                                 ?? new List<ISideStripProvider>();

            return new LoadedPlugin
            {
                Manifest = manifest,
                Directory = dir,
                Status = PluginLoadStatus.Loaded,
                Instance = instance,
                LoadContext = context,
                Host = host,
                Commands = commands,
                SideStripProviders = stripProviders
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
