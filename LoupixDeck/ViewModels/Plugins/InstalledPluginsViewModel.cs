using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Localization;
using LoupixDeck.Models;
using LoupixDeck.Services.Plugins;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels.Plugins;

/// <summary>
/// The installed-plugins page: the list on the left and the selected plugin's settings on the
/// right. Everything the old Settings page built by hand in code-behind lives here instead, so
/// the view is only markup.
/// </summary>
public sealed partial class InstalledPluginsViewModel : ViewModelBase
{
    private readonly PluginsWindowViewModel _owner;

    public InstalledPluginsViewModel(PluginsWindowViewModel owner)
    {
        _owner = owner;
        Refresh();
    }

    /// <summary>Every plugin that can run here, in list order.</summary>
    private readonly List<InstalledPluginRowViewModel> _allRows = [];

    /// <summary>What the list shows: <see cref="_allRows"/> narrowed by <see cref="SearchText"/>.</summary>
    public ObservableCollection<InstalledPluginRowViewModel> Rows { get; } = [];

    public bool HasRows => Rows.Count > 0;

    /// <summary>Count in the group header and on the rail; the installed set, not the filtered
    /// one, so searching does not make it look as if plugins disappeared.</summary>
    public int InstalledCount => _allRows.Count;

    /// <summary>Header above the list. Built here rather than with a StringFormat binding: the
    /// loc markup extension yields a binding, which a StringFormat cannot take.</summary>
    public string InstalledHeader => Loc.Tr("Plugins_InstalledCount", InstalledCount);

    /// <summary>Filters by plugin name only, case-insensitive, as you type. Deliberately not
    /// over commands: a plugin is looked up by its name here.</summary>
    [ObservableProperty]
    public partial string SearchText { get; set; }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    public bool HasSearch => !string.IsNullOrWhiteSpace(SearchText);

    /// <summary>True when a search is active but matches nothing.</summary>
    public bool HasNoMatches => HasSearch && Rows.Count == 0 && _allRows.Count > 0;

    private void ApplyFilter()
    {
        InstalledPluginRowViewModel selected = SelectedPlugin;

        Rows.Clear();
        foreach (InstalledPluginRowViewModel row in _allRows)
        {
            if (!HasSearch || row.Name?.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) == true)
                Rows.Add(row);
        }

        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(HasSearch));
        OnPropertyChanged(nameof(HasNoMatches));

        // Keep the detail pane on the plugin it was showing when that row is still listed.
        SelectedPlugin = Rows.Contains(selected) ? selected : Rows.FirstOrDefault();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(CanRemoveSelected))]
    [NotifyPropertyChangedFor(nameof(RemoveLabel))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    public partial InstalledPluginRowViewModel SelectedPlugin { get; set; }

    [ObservableProperty]
    public partial PluginDetailViewModel Detail { get; set; }

    public bool HasSelection => SelectedPlugin != null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string StatusText { get; set; }

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);

    /// <summary>Shown once an action reported that it only finishes on the next start.</summary>
    [ObservableProperty]
    public partial bool ShowRestartHint { get; set; }

    /// <summary>Guards the list while an enable/disable is in flight, so rapid clicks cannot
    /// race the reload coordinator.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    partial void OnSelectedPluginChanged(InstalledPluginRowViewModel value)
    {
        Detail = value == null ? null : new PluginDetailViewModel(value.Plugin);
    }

    /// <summary>
    /// Rebuilds the list from the manager's live snapshot. Called after every action that can
    /// change it - enable, disable, install, remove - and when the store reports it installed
    /// something.
    /// </summary>
    public void Refresh()
    {
        string selectedId = SelectedPlugin?.Id;

        _allRows.Clear();
        foreach (LoadedPlugin plugin in _owner.Plugins)
        {
            // A plugin whose manifest targets the other OS can never load here, so it is hidden
            // instead of shown as a dead row with a toggle that does nothing.
            if (!PluginManager.SupportsCurrentPlatform(plugin.Manifest))
                continue;

            string id = plugin.Manifest?.Id;
            _allRows.Add(new InstalledPluginRowViewModel(plugin, id != null && IsEnabled(id), SetEnabledAsync));
        }

        ApplyFilter();
        OnPropertyChanged(nameof(InstalledCount));
        OnPropertyChanged(nameof(InstalledHeader));

        SelectedPlugin = Rows.FirstOrDefault(row =>
                             string.Equals(row.Id, selectedId, StringComparison.OrdinalIgnoreCase))
                         ?? Rows.FirstOrDefault();
    }

    /// <summary>Brings one plugin up, e.g. when the store jumps here to set it up.</summary>
    public void SelectPlugin(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
            return;

        // A search that hides the wanted plugin would make the jump look like it did nothing.
        SearchText = null;

        InstalledPluginRowViewModel row = Rows.FirstOrDefault(r =>
            string.Equals(r.Id, pluginId, StringComparison.OrdinalIgnoreCase));

        if (row != null)
            SelectedPlugin = row;
    }

    private bool IsEnabled(string id) =>
        _owner.Config?.EnabledPlugins?.Any(e => string.Equals(e, id, StringComparison.OrdinalIgnoreCase))
        ?? false;

    private async Task SetEnabledAsync(InstalledPluginRowViewModel row, bool enabled)
    {
        if (!row.CanToggle)
            return;

        // The reload coordinator owns the EnabledPlugins mutation and the live load/unload.
        IsBusy = true;
        try
        {
            PluginActionResult result = enabled
                ? await _owner.EnablePluginAsync(row.Id)
                : await _owner.DisablePluginAsync(row.Id);

            ReportResult(result);
        }
        finally
        {
            IsBusy = false;
        }

        Refresh();
    }

    // ---------- Install / remove ----------

    public IAsyncRelayCommand InstallFromZipCommand => field ??= Relay.Create(InstallFromZipAsync);

    public IAsyncRelayCommand RemoveCommand => field ??= Relay.Create(RemoveAsync, () => HasSelection);

    public IRelayCommand OpenPluginsFolderCommand => _owner.OpenPluginsFolderCommand;

    private async Task InstallFromZipAsync()
    {
        string zipPath = await FileDialogHelper.OpenZipDialog(WindowHelper.GetActiveWindow());
        if (string.IsNullOrEmpty(zipPath))
            return;

        ReportResult(await _owner.InstallPluginFromZipAsync(zipPath));

        // A freshly installed plugin loads live - rebuild the list so it appears.
        Refresh();
    }

    /// <summary>
    /// A user copy overriding a built-in is reset, not removed (the bundled version takes
    /// over); a plain built-in cannot be touched at all.
    /// </summary>
    public bool CanRemoveSelected => SelectedPlugin is { } row
                                     && (row.Plugin.BundledFallbackVersion != null || !row.Plugin.IsBundled);

    public string RemoveLabel => SelectedPlugin?.Plugin.BundledFallbackVersion != null
        ? Loc.Tr("Plugins_ResetToBuiltIn")
        : Loc.Tr("Settings_Remove");

    private async Task RemoveAsync()
    {
        if (SelectedPlugin is not { } row)
        {
            StatusText = Loc.Tr("Settings_SelectAPluginToRemove");
            return;
        }

        LoadedPlugin plugin = row.Plugin;
        string name = plugin.Manifest?.Name ?? plugin.Directory;
        bool isOverride = plugin.BundledFallbackVersion != null;

        bool confirmed = isOverride
            ? await ConfirmDialogHelper.AskYesNoAsync(WindowHelper.GetActiveWindow(),
                Loc.Tr("Confirm_ResetPluginTitle"),
                Loc.Tr("Confirm_ResetPluginMessage", name, plugin.BundledFallbackVersion))
            : await ConfirmDialogHelper.AskYesNoAsync(WindowHelper.GetActiveWindow(),
                Loc.Tr("Confirm_RemovePluginTitle"),
                Loc.Tr("Confirm_RemovePluginMessage", name));

        if (!confirmed)
            return;

        PluginActionResult result = await _owner.RemovePluginAsync(plugin);

        if (result.Success)
        {
            // Rebuild from the live plugin list: the row disappears for a removal, and reappears
            // as the bundled version when an override was reset.
            SelectedPlugin = null;
            Refresh();
        }

        ReportResult(result);
    }

    private void ReportResult(PluginActionResult result)
    {
        if (result == null)
            return;

        StatusText = result.Message;

        // Something changed here; the store shows it on its next visit.
        _owner.PluginStore.Invalidate();

        if (result is { Success: true, RequiresRestart: true })
            ShowRestartHint = true;

        OnPropertyChanged(nameof(CanRemoveSelected));
        OnPropertyChanged(nameof(RemoveLabel));
    }
}
