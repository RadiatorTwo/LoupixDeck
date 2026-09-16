using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Localization;
using LoupixDeck.Models;
using LoupixDeck.Services;
using LoupixDeck.Services.PluginStore;
using LoupixDeck.Services.Plugins;
using LoupixDeck.Services.Updates;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels;

/// <summary>
/// The Plugin Store settings page (issue #234): every catalog plugin with its state on this machine, and
/// Install / Update / Remove. Installs go through the same reload coordinator as a zip install, so a plugin
/// loads live when it can and is swapped in on the next start when it can't.
/// </summary>
public sealed partial class PluginStoreViewModel(
    IPluginStoreService store,
    IPluginReloadService pluginReload,
    IDialogService dialogService) : ViewModelBase
{
    private bool _loaded;

    public ObservableCollection<PluginStoreRowViewModel> Items { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string StatusText { get; set; }

    public bool HasStatus => !string.IsNullOrEmpty(StatusText);

    /// <summary>Why the list could not be fully loaded (offline, GitHub's request limit); shown above the list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    public partial string NoticeText { get; set; }

    public bool HasNotice => !string.IsNullOrEmpty(NoticeText);

    [ObservableProperty]
    public partial bool ShowRestartHint { get; set; }

    /// <summary>Plugin id to bring to the top of the list, e.g. one a config needs but is missing.</summary>
    [ObservableProperty]
    public partial string HighlightedPluginId { get; set; }

    public IAsyncRelayCommand RefreshCommand => field ??= Relay.Create(() => LoadAsync(force: true), () => !IsLoading);

    public IAsyncRelayCommand<PluginStoreRowViewModel> InstallCommand =>
        field ??= Relay.Create<PluginStoreRowViewModel>(InstallAsync);

    public IAsyncRelayCommand<PluginStoreRowViewModel> RemoveCommand =>
        field ??= Relay.Create<PluginStoreRowViewModel>(RemoveAsync);

    /// <summary>Raised after a plugin was installed, updated or removed, so the Plugins page can rebuild its list.</summary>
    public event Action PluginsChanged;

    /// <summary>Makes the next visit of the page reload the list, after the Plugins page changed something.</summary>
    public void Invalidate()
    {
        _loaded = false;
    }

    /// <summary>Loads the list the first time the page is shown.</summary>
    public Task EnsureLoadedAsync()
    {
        return _loaded ? Task.CompletedTask : LoadAsync(force: false);
    }

    private async Task LoadAsync(bool force)
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        StatusText = Loc.Tr("PluginStore_Loading");
        try
        {
            PluginStoreResult result = await store.GetItemsAsync(force);
            _loaded = true;

            IEnumerable<PluginStoreItem> visible = result.Items
                .Where(i => i.Installed is not null || i.Entry.SupportsCurrentPlatform())
                .OrderByDescending(i => string.Equals(i.Entry.Id, HighlightedPluginId, StringComparison.OrdinalIgnoreCase))
                .ThenBy(i => i.Entry.DisplayName, StringComparer.CurrentCultureIgnoreCase);

            Items.Clear();
            foreach (PluginStoreItem item in visible)
            {
                PluginStoreRowViewModel row = new(item, string.Equals(item.Entry.Id, HighlightedPluginId,
                    StringComparison.OrdinalIgnoreCase));
                Items.Add(row);
                _ = row.LoadIconAsync();
            }

            NoticeText = result.Error;
            StatusText = Items.Count == 0 && result.Error is null ? Loc.Tr("PluginStore_Empty") : null;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task InstallAsync(PluginStoreRowViewModel row)
    {
        PluginReleaseCandidate candidate = row?.Item.Available;
        if (candidate is null || row.IsBusy)
        {
            return;
        }

        // Release notes first; nothing is downloaded unless the user confirms.
        DialogResult confirmed = await dialogService.ShowDialogAsync<PluginReleaseNotesViewModel, DialogResult>(
            vm => vm.Initialize(row.Item));
        if (confirmed is not { IsConfirmed: true })
        {
            return;
        }

        row.IsBusy = true;
        row.Progress = 0;
        StatusText = Loc.Tr("PluginStore_Downloading", row.Name, candidate.Version);
        try
        {
            Progress<double> progress = new(value => row.Progress = value * 100);
            PluginDownloadResult download = await store.DownloadAsync(candidate, progress);
            if (!download.Success)
            {
                StatusText = download.Error;
                return;
            }

            PluginActionResult result;
            try
            {
                result = await pluginReload.InstallAsync(download.PackagePath);
            }
            finally
            {
                TryDeleteFile(download.PackagePath);
            }

            StatusText = result.Message;
            if (!result.Success)
            {
                return;
            }

            store.MarkInstalled(row.Item.Entry, candidate);
            if (result.RequiresRestart)
            {
                store.MarkRestartRequired(row.Item.Entry.Id);
                ShowRestartHint = true;
            }

            PluginsChanged?.Invoke();
        }
        finally
        {
            row.IsBusy = false;
        }

        string message = StatusText;
        await LoadAsync(force: false);
        StatusText = message;
    }

    private async Task RemoveAsync(PluginStoreRowViewModel row)
    {
        LoadedPlugin plugin = row?.Item.Installed;
        if (plugin is null || row.IsBusy)
        {
            return;
        }

        bool confirmed = await ConfirmDialogHelper.AskYesNoAsync(WindowHelper.GetActiveWindow(),
            Loc.Tr("Confirm_RemovePluginTitle"), Loc.Tr("Confirm_RemovePluginMessage", row.Name));
        if (!confirmed)
        {
            return;
        }

        row.IsBusy = true;
        try
        {
            PluginActionResult result = await pluginReload.RemoveAsync(plugin);
            StatusText = result.Message;
            if (result is { Success: true, RequiresRestart: true })
            {
                store.MarkRestartRequired(row.Item.Entry.Id);
                ShowRestartHint = true;
            }

            PluginsChanged?.Invoke();
        }
        finally
        {
            row.IsBusy = false;
        }

        string message = StatusText;
        await LoadAsync(force: false);
        StatusText = message;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Temp cleanup is best-effort.
        }
    }
}

/// <summary>One plugin row on the Plugin Store page.</summary>
public sealed partial class PluginStoreRowViewModel(PluginStoreItem item, bool isHighlighted) : ViewModelBase
{
    public PluginStoreItem Item { get; } = item;

    public bool IsHighlighted { get; } = isHighlighted;

    public string Name => Item.Entry.DisplayName;

    public string Author => string.IsNullOrWhiteSpace(Item.Entry.Author) ? null : Loc.Tr("PluginStore_ByAuthor", Item.Entry.Author);

    public string Description => Item.Entry.Description;

    public string VersionText => Item.Status switch
    {
        PluginStoreStatus.UpdateAvailable => $"{Item.InstalledVersion} → {Item.Available.Version}",
        _ when Item.Installed is not null => Item.InstalledVersion,
        _ => Item.Available?.Version.ToString() ?? Item.Newest?.Version.ToString() ?? string.Empty
    };

    public string StatusText => Item.Status switch
    {
        PluginStoreStatus.NotInstalled => Loc.Tr("PluginStore_StatusNotInstalled"),
        PluginStoreStatus.Installed => Loc.Tr("PluginStore_StatusInstalled"),
        PluginStoreStatus.UpdateAvailable => Loc.Tr("PluginStore_StatusUpdateAvailable"),
        PluginStoreStatus.ManuallyInstalled => Loc.Tr("PluginStore_StatusManuallyInstalled"),
        PluginStoreStatus.RequiresNewerApp => Loc.Tr("PluginStore_StatusRequiresNewerApp", Item.Newest?.Version),
        PluginStoreStatus.NoRelease => Loc.Tr("PluginStore_StatusNoRelease"),
        PluginStoreStatus.RestartRequired => Loc.Tr("PluginStore_StatusRestartRequired"),
        _ => Loc.Tr("PluginStore_StatusUnavailable")
    };

    public bool IsStatusOk => Item.Status == PluginStoreStatus.Installed;

    public bool IsStatusWarn => Item.Status is PluginStoreStatus.UpdateAvailable or PluginStoreStatus.RestartRequired
        or PluginStoreStatus.RequiresNewerApp;

    public bool IsStatusNeutral => !IsStatusOk && !IsStatusWarn;

    /// <summary>Extra line under the status: an error, or a newer release this app cannot load yet.</summary>
    public string DetailText => Item.Error
                                ?? (Item.Status is PluginStoreStatus.Installed or PluginStoreStatus.UpdateAvailable
                                    && Item.NewerNeedsApp
                                        ? Loc.Tr("PluginStore_NewerNeedsApp", Item.Newest.Version)
                                        : null);

    public bool HasDetail => !string.IsNullOrEmpty(DetailText);

    public bool CanInstall => Item.Status == PluginStoreStatus.NotInstalled;

    public bool CanUpdate => Item.Status == PluginStoreStatus.UpdateAvailable;

    /// <summary>Only folders in the user plugin directory can be deleted; a bundled copy is read-only.</summary>
    public bool CanRemove => Item.Installed is { IsBundled: false }
                             && Item.Status is not PluginStoreStatus.RestartRequired;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsBusy { get; set; }

    public bool IsIdle => !IsBusy;

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIcon))]
    public partial Bitmap Icon { get; set; }

    public bool HasIcon => Icon is not null;

    public async Task LoadIconAsync()
    {
        string url = Item.Entry.Icon;
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out Uri uri)
                                           || uri.Scheme != Uri.UriSchemeHttps)
        {
            return;
        }

        try
        {
            byte[] bytes = await FileDownloader.DownloadBytesAsync(url, CancellationToken.None);
            using MemoryStream stream = new(bytes);
            Icon = new Bitmap(stream);
        }
        catch (Exception ex)
        {
            // An icon is decoration; a broken one never gets in the way of the list.
            Console.WriteLine($"[PluginStore] Could not load the icon of {Item.Entry.Id}: {ex.Message}");
        }
    }
}
