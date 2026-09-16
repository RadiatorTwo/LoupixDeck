using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Models;
using LoupixDeck.Services;
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
    private readonly IPluginManager _pluginManager;

    /// <summary>Every running device. Which one a plugin is enabled on is the user's choice, so
    /// the page shows it and resolves the reload coordinator from that device's container.</summary>
    public IDeviceHostRegistry Hosts { get; }

    /// <summary>
    /// All discovered plugins — drives the installed list. Read live from the manager (its
    /// list is swapped on hot-reload), never cached, so the UI re-reads the current snapshot
    /// after an enable/disable/install/remove.
    /// </summary>
    public IReadOnlyList<LoadedPlugin> Plugins => _pluginManager.Plugins;

    /// <summary>The Plugin Store page (issue #234).</summary>
    public PluginStoreViewModel PluginStore { get; }

    /// <summary>The installed-plugins page.</summary>
    public InstalledPluginsViewModel Installed { get; }

    public PluginsWindowViewModel(IPluginManager pluginManager,
        IDeviceHostRegistry hosts,
        PluginStoreViewModel pluginStore)
    {
        _pluginManager = pluginManager;
        Hosts = hosts;
        PluginStore = pluginStore;
        Installed = new InstalledPluginsViewModel(this);

        // The store installs, updates and removes through the same coordinator, so the
        // installed list has to follow what it did.
        PluginStore.PluginsChanged += OnPluginsChanged;

        // The store jumps here to set a freshly installed plugin up.
        PluginStore.SetupRequested += pluginId =>
        {
            Installed.SelectPlugin(pluginId);
            CurrentPage = PluginsPage.Installed;
        };
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

    // ---------- Rail counts ----------

    /// <summary>Shown after the "Plugins" rail entry.</summary>
    public int InstalledCount => Installed.InstalledCount;

    /// <summary>Update badge on the "Plugin Store" entry. Reads zero until a catalog has been
    /// loaded once - by opening the store page or by the background check.</summary>
    public int UpdateCount => PluginStore.AvailableUpdateCount;

    public bool HasUpdates => UpdateCount > 0;

    private void OnPluginsChanged()
    {
        Installed.Refresh();
        OnPropertyChanged(nameof(InstalledCount));
        OnPropertyChanged(nameof(UpdateCount));
        OnPropertyChanged(nameof(HasUpdates));
    }

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

}
