using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Models;
using LoupixDeck.Services.Plugins;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels.Plugins;

/// <summary>
/// The Plugins window: installed plugins and the Plugin Store, behind one rail. Both used to
/// be pages of the Settings window, which left them competing for space with the device
/// settings and hid the store's actions in a page header.
/// </summary>
public partial class PluginsWindowViewModel : DialogViewModelBase<DialogResult>
{
    private readonly IPluginReloadService _pluginReload;
    private readonly IPluginManager _pluginManager;

    /// <summary>The enabled set is persisted per device (LoupedeckConfig.EnabledPlugins), so
    /// the list's checkboxes read this device's config — exactly as the Settings page did.</summary>
    public LoupedeckConfig Config { get; }

    /// <summary>
    /// All discovered plugins — drives the installed list. Read live from the manager (its
    /// list is swapped on hot-reload), never cached, so the UI re-reads the current snapshot
    /// after an enable/disable/install/remove.
    /// </summary>
    public IReadOnlyList<LoadedPlugin> Plugins => _pluginManager.Plugins;

    /// <summary>The Plugin Store page (issue #234).</summary>
    public PluginStoreViewModel PluginStore { get; }

    public PluginsWindowViewModel(LoupedeckConfig config,
        IPluginManager pluginManager,
        IPluginReloadService pluginReload,
        PluginStoreViewModel pluginStore)
    {
        Config = config;
        _pluginManager = pluginManager;
        _pluginReload = pluginReload;
        PluginStore = pluginStore;
    }

    // ───────── Page navigation ─────────

    private PluginsPage _currentPage;

    public PluginsPage CurrentPage
    {
        get => _currentPage;
        set
        {
            // The store reaches the network, so it only loads once its page is opened.
            if (SetProperty(ref _currentPage, value) && value == PluginsPage.Store)
                _ = PluginStore.EnsureLoadedAsync();
        }
    }

    public IRelayCommand NavigateCommand => field ??= Relay.Create<PluginsPage>(page => CurrentPage = page);

    /// <summary>Opens the window on the Plugin Store page, optionally with one plugin brought to the top.</summary>
    public void OpenPluginStore(string highlightedPluginId = null)
    {
        PluginStore.HighlightedPluginId = highlightedPluginId;
        CurrentPage = PluginsPage.Store;
    }

    // ───────── Plugin actions ─────────

    /// <summary>
    /// Opens the user plugins folder in the OS file manager, creating it first
    /// if missing. This is the per-build config plugins dir
    /// (<c>~/.config/LoupixDeck[/debug]/plugins</c>), where users drop their own
    /// plugins. UseShellExecute=true routes a directory path through Explorer on
    /// Windows / xdg-open on Linux.
    /// </summary>
    public IRelayCommand OpenPluginsFolderCommand => field ??= Relay.Create(OpenPluginsFolder);

    private void OpenPluginsFolder()
    {
        try
        {
            string dir = System.IO.Path.Combine(FileDialogHelper.GetConfigDir(), "plugins");
            System.IO.Directory.CreateDirectory(dir);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch { }
    }

    /// <summary>Installs (or updates) a plugin from the given zip and loads it live.</summary>
    public Task<PluginActionResult> InstallPluginFromZipAsync(string zipPath) =>
        _pluginReload.InstallAsync(zipPath);

    /// <summary>Unloads and removes an installed (user) plugin live.</summary>
    public Task<PluginActionResult> RemovePluginAsync(LoadedPlugin plugin) =>
        _pluginReload.RemoveAsync(plugin);

    /// <summary>Loads a plugin live (no restart).</summary>
    public Task<PluginActionResult> EnablePluginAsync(string pluginId) =>
        _pluginReload.EnableAsync(pluginId);

    /// <summary>Unloads a plugin live (no restart).</summary>
    public Task<PluginActionResult> DisablePluginAsync(string pluginId) =>
        _pluginReload.DisableAsync(pluginId);
}
