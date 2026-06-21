using System.Runtime.Loader;
using Avalonia.Threading;
using LoupixDeck.Controllers;
using LoupixDeck.Services.Commands;
using LoupixDeck.Services.FolderNavigation;

namespace LoupixDeck.Services.Plugins;

/// <summary>
/// Orchestrates plugin enable/disable/install/remove so they take effect WITHOUT a
/// restart. Every flow runs on the UI thread, serialized by a semaphore, and ends
/// with the same refresh: rebuild the command registry, rescan dynamic-text buttons,
/// and repaint the current touch page. Before unloading a plugin it tears down any
/// references that would pin its collectible load context (exclusive mode, folder
/// navigation). The collectible unload is best-effort — file deletion keeps the
/// <see cref="PluginInstaller"/> restart fallback for assemblies that stay locked.
/// </summary>
public interface IPluginReloadService
{
    Task<PluginActionResult> EnableAsync(string pluginId);
    Task<PluginActionResult> DisableAsync(string pluginId);

    /// <summary>Installs (or updates) from a zip and loads it live when possible.</summary>
    Task<PluginActionResult> InstallAsync(string zipPath);

    /// <summary>Unloads a plugin live, then deletes it (or defers the delete to restart).</summary>
    Task<PluginActionResult> RemoveAsync(LoadedPlugin plugin);
}

/// <inheritdoc cref="IPluginReloadService"/>
public sealed class PluginReloadService(
    IPluginManager pluginManager,
    ICommandRegistry commandRegistry,
    ISideStripProviderRegistry sideStripRegistry,
    IDynamicTextManager dynamicText,
    Animation.IButtonAnimationManager buttonAnimation,
    IExclusiveModeService exclusiveMode,
    IFolderNavigationService folderNav,
    IDeviceController deviceController,
    IPluginInstaller installer,
    Models.LoupedeckConfig config) : IPluginReloadService
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Task<PluginActionResult> EnableAsync(string pluginId) => RunAsync(async () =>
    {
        EnsureEnabled(pluginId); // gate in LoadOne reads EnabledPlugins live
        var loaded = pluginManager.LoadPlugin(pluginId);
        await RefreshAsync();

        if (loaded == null)
            return PluginActionResult.Fail($"Could not find plugin '{pluginId}'.");

        var name = Name(loaded, pluginId);
        return loaded.Status == PluginLoadStatus.Loaded
            ? PluginActionResult.Ok($"Enabled '{name}'.", requiresRestart: false, pluginId: pluginId)
            : PluginActionResult.Fail(
                $"'{name}' could not be enabled: {loaded.FailureReason ?? loaded.Status.ToString()}");
    });

    public Task<PluginActionResult> DisableAsync(string pluginId) => RunAsync(async () =>
    {
        var plugin = Find(pluginId);
        var name = plugin != null ? Name(plugin, pluginId) : pluginId;

        // Tear down ownership first (needs the live context), drop the enable flag,
        // then re-resolve: with the flag off, LoadPlugin re-adds a Disabled entry
        // (no context) after unloading the live one — so the plugin stays visible in
        // the list and remains re-enableable, instead of vanishing.
        TearDownOwnership(plugin);
        RemoveEnabled(pluginId);
        pluginManager.LoadPlugin(pluginId);
        await RefreshAsync();

        return PluginActionResult.Ok($"Disabled '{name}'.", requiresRestart: false, pluginId: pluginId);
    });

    public Task<PluginActionResult> InstallAsync(string zipPath) => RunAsync(async () =>
    {
        // File work (extract/validate/copy/stage) happens off the UI thread inside.
        var result = await installer.InstallFromZipAsync(zipPath);
        if (!result.Success)
            return result;

        var id = result.PluginId;

        // Stop any currently-loaded old version so a live update reloads cleanly;
        // a no-op for a brand-new install (nothing loaded yet).
        var existing = Find(id);
        if (existing is { Status: PluginLoadStatus.Loaded })
        {
            TearDownOwnership(existing);
            pluginManager.UnloadPlugin(id);
        }

        if (result.RequiresRestart)
        {
            // Staged update — old is unloaded (commands gone), new files swap on restart.
            await RefreshAsync();
            return result;
        }

        var loaded = pluginManager.LoadPlugin(id);
        await RefreshAsync();

        var name = loaded != null ? Name(loaded, id) : id;
        if (loaded?.Status == PluginLoadStatus.Loaded)
            return PluginActionResult.Ok($"Installed and loaded '{name}'.", requiresRestart: false, pluginId: id);

        return PluginActionResult.Ok(
            $"{result.Message} It could not be loaded live ({loaded?.FailureReason ?? "unknown"}); restart to retry.",
            requiresRestart: true, pluginId: id);
    });

    public Task<PluginActionResult> RemoveAsync(LoadedPlugin plugin) => RunAsync(async () =>
    {
        var id = plugin?.Manifest?.Id;

        // Tear down + unload live so commands stop now.
        TearDownOwnership(plugin);
        if (!string.IsNullOrWhiteSpace(id))
            pluginManager.UnloadPlugin(id);

        // Drop every remaining reference into the old context — the registry's
        // RegisteredCommands and the dynamic-text entries — BEFORE nudging the GC,
        // otherwise the assembly stays rooted and the folder can't be deleted live.
        commandRegistry.Initialize();
        dynamicText.Rescan();
        buttonAnimation.Rescan();
        TryCollectUnloaded();

        // Now attempt the delete; a cleanly-collected plugin is removed live, a
        // still-locked one falls back to the .pending-removals marker (next startup).
        var result = installer.Remove(plugin);
        await deviceController.RedrawCurrentTouchPage();
        return result;
    });

    // ───────── internals ─────────

    /// <summary>Registry rebuild + dynamic-text rescan + current-page repaint, plus
    /// side-strip provider rebuild and re-attachment (so a reloaded provider re-binds
    /// and orphaned bindings fall back to segmented).</summary>
    private async Task RefreshAsync()
    {
        commandRegistry.Initialize();
        sideStripRegistry.Rebuild();
        dynamicText.Rescan();
        buttonAnimation.Rescan();
        await deviceController.RedrawCurrentTouchPage();
        await deviceController.RefreshSideStrips();
    }

    /// <summary>
    /// Releases references into the plugin's load context so it can actually unload:
    /// exclusive mode if this plugin owns it, and folder navigation entirely if any
    /// folder is open (a plugin adapter chain may be on the stack).
    /// </summary>
    private void TearDownOwnership(LoadedPlugin plugin)
    {
        if (plugin == null)
            return;

        var current = exclusiveMode.Current;
        if (current != null && Owns(plugin, current))
            exclusiveMode.Exit(current);

        // A live side-strip provider attached to a strip roots this plugin's load
        // context. Detach all (cheap; RefreshAsync re-attaches the still-loaded ones).
        deviceController.DetachAllSideStripProviders();

        if (folderNav.IsActive)
            folderNav.ExitAll().GetAwaiter().GetResult(); // completes synchronously
    }

    /// <summary>
    /// The standard collectible-AssemblyLoadContext unload nudge: two collections
    /// around a finalizer drain so a cleanly-unloaded plugin's assembly is reclaimed
    /// and its files unlocked. Best-effort — a plugin that leaks a reference simply
    /// isn't collected, and the caller falls back to the pending-removal marker.
    /// </summary>
    private static void TryCollectUnloaded()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static bool Owns(LoadedPlugin plugin, object obj)
    {
        if (plugin?.LoadContext == null || obj == null)
            return false;

        var objContext = AssemblyLoadContext.GetLoadContext(obj.GetType().Assembly);
        return ReferenceEquals(objContext, plugin.LoadContext);
    }

    private LoadedPlugin Find(string id) =>
        pluginManager.Plugins.FirstOrDefault(
            p => string.Equals(p.Manifest?.Id, id, StringComparison.OrdinalIgnoreCase));

    private void EnsureEnabled(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;

        config.EnabledPlugins ??= [];
        if (!config.EnabledPlugins.Any(e => string.Equals(e, id, StringComparison.OrdinalIgnoreCase)))
            config.EnabledPlugins.Add(id);
    }

    private void RemoveEnabled(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;

        config.EnabledPlugins?.RemoveAll(e => string.Equals(e, id, StringComparison.OrdinalIgnoreCase));
    }

    private static string Name(LoadedPlugin plugin, string fallbackId) =>
        string.IsNullOrWhiteSpace(plugin?.Manifest?.Name) ? fallbackId : plugin.Manifest.Name;

    private async Task<PluginActionResult> RunAsync(Func<Task<PluginActionResult>> action)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return Dispatcher.UIThread.CheckAccess()
                ? await action()
                : await Dispatcher.UIThread.InvokeAsync(action);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"PluginReloadService: operation failed: {ex.Message}");
            return PluginActionResult.Fail($"Plugin operation failed: {ex.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }
}
