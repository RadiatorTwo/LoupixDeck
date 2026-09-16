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

namespace LoupixDeck.ViewModels.Plugins;

/// <summary>
/// The Plugin Store settings page (issue #234): every catalog plugin with its state on this machine, and
/// Install / Update / Remove. Installs go through the same reload coordinator as a zip install, so a plugin
/// loads live when it can and is swapped in on the next start when it can't.
/// </summary>
public sealed partial class PluginStoreViewModel(
    IPluginStoreService store,
    IPluginReloadService pluginReload,
    IUpdateService updateService,
    IDialogService dialogService) : ViewModelBase
{
    private bool _loaded;

    /// <summary>Everything the catalog offers for this system, in list order.</summary>
    private readonly List<PluginStoreRowViewModel> _allItems = [];

    /// <summary>What the page shows: <see cref="_allItems"/> narrowed by <see cref="SearchText"/>.</summary>
    public ObservableCollection<PluginStoreRowViewModel> Items { get; } = [];

    /// <summary>Filters the catalog by plugin name, case-insensitive, as you type.</summary>
    [ObservableProperty]
    public partial string SearchText { get; set; }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    public bool HasSearch => !string.IsNullOrWhiteSpace(SearchText);

    public bool HasNoMatches => HasSearch && Items.Count == 0 && _allItems.Count > 0;

    private void ApplyFilter()
    {
        Items.Clear();
        foreach (PluginStoreRowViewModel row in _allItems)
        {
            if (!HasSearch || row.Name?.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) == true)
                Items.Add(row);
        }

        OnPropertyChanged(nameof(HasSearch));
        OnPropertyChanged(nameof(HasNoMatches));
    }

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

    /// <summary>
    /// Says that the list is not what the server currently publishes — the offline copy, or a list too old to
    /// carry version information. Shown above the error, which says what went wrong.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStaleNotice))]
    public partial string StaleNoticeText { get; set; }

    public bool HasStaleNotice => !string.IsNullOrEmpty(StaleNoticeText);

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

    /// <summary>Stops a download that is already running; the tile goes back to what it was.</summary>
    public IRelayCommand<PluginStoreRowViewModel> CancelCommand =>
        field ??= Relay.Create<PluginStoreRowViewModel>(row => row?.Cancel());

    /// <summary>Shows the release notes without offering to install anything.</summary>
    public IAsyncRelayCommand<PluginStoreRowViewModel> ReleaseNotesCommand =>
        field ??= Relay.Create<PluginStoreRowViewModel>(ShowReleaseNotesAsync);

    /// <summary>Jumps to the installed page with this plugin selected, to set it up.</summary>
    public IRelayCommand<PluginStoreRowViewModel> SetupCommand =>
        field ??= Relay.Create<PluginStoreRowViewModel>(row => SetupRequested?.Invoke(row?.Item.Entry.Id));

    /// <summary>Puts a hand-copied plugin under the store's wing, so it gets updates.</summary>
    public IAsyncRelayCommand<PluginStoreRowViewModel> AdoptCommand =>
        field ??= Relay.Create<PluginStoreRowViewModel>(AdoptAsync);

    /// <summary>For a plugin whose newest release needs a newer LoupixDeck than this one.</summary>
    public IAsyncRelayCommand<PluginStoreRowViewModel> CheckAppUpdateCommand =>
        field ??= Relay.Create<PluginStoreRowViewModel>(CheckAppUpdateAsync);

    /// <summary>Restarts the app so a change that only finishes on the next start takes effect.</summary>
    public IAsyncRelayCommand RestartCommand => field ??= Relay.Create(RestartAsync);

    private async Task RestartAsync()
    {
        bool confirmed = await ConfirmDialogHelper.AskYesNoAsync(WindowHelper.GetActiveWindow(),
            Loc.Tr("Plugins_ConfirmRestartTitle"), Loc.Tr("Plugins_ConfirmRestartMessage"));
        if (!confirmed)
        {
            return;
        }

        if (!AppRestart.BeginRestart())
        {
            StatusText = Loc.Tr("Plugins_RestartFailed");
            return;
        }

        // The successor is waiting for this process, so shut down the ordinary way: devices are
        // stopped cleanly and the single-instance handle is released.
        if (WindowHelper.GetMainWindow() is Views.MainWindow main)
        {
            main.QuitApplication();
            return;
        }

        Environment.Exit(0);
    }

    private async Task ShowReleaseNotesAsync(PluginStoreRowViewModel row)
    {
        if (row?.Item is null || !row.HasReleaseNotes)
        {
            return;
        }

        await dialogService.ShowDialogAsync<PluginReleaseNotesViewModel, DialogResult>(vm =>
        {
            vm.Initialize(row.Item, confirmation: false);
            _ = vm.LoadNotesAsync();
        });
    }

    private async Task AdoptAsync(PluginStoreRowViewModel row)
    {
        if (row?.Item.Installed is null)
        {
            return;
        }

        if (!store.Adopt(row.Item.Entry, row.Item.Installed))
        {
            StatusText = Loc.Tr("PluginStore_AdoptFailed", row.Name);
            return;
        }

        // The marker carries no tag, so the status is recomputed from the versions: the plugin
        // becomes store-managed and may show an update straight away.
        await LoadAsync(force: false);
    }

    private async Task CheckAppUpdateAsync(PluginStoreRowViewModel row)
    {
        UpdateCheckResult result = await updateService.CheckAsync(manual: true);

        if (result.Status == UpdateCheckStatus.Failed)
        {
            StatusText = Loc.Tr("PluginStore_AppUpdateCheckFailed");
            return;
        }

        if (updateService.AvailableUpdate is null)
        {
            StatusText = Loc.Tr("PluginStore_NoAppUpdate", row?.Name);
            return;
        }

        await dialogService.ShowDialogAsync<UpdateDialogViewModel, DialogResult>(
            vm => vm.Initialize(updateService.AvailableUpdate));
    }

    /// <summary>Raised after a plugin was installed, updated or removed, so the Plugins page can rebuild its list.</summary>
    public event Action PluginsChanged;

    /// <summary>Raised when a tile asks for the installed page, with that plugin selected.</summary>
    public event Action<string> SetupRequested;

    /// <summary>Drives the update badge on the rail. Reads zero until a catalog has been loaded
    /// once, by opening this page or by the background check.</summary>
    public int AvailableUpdateCount => store.AvailableUpdates?.Count ?? 0;

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

            _allItems.Clear();
            foreach (PluginStoreItem item in visible)
            {
                PluginStoreRowViewModel row = new(item, string.Equals(item.Entry.Id, HighlightedPluginId,
                    StringComparison.OrdinalIgnoreCase));
                _allItems.Add(row);
                _ = row.LoadIconAsync();
            }

            ApplyFilter();
            OnPropertyChanged(nameof(AvailableUpdateCount));

            NoticeText = result.Error;
            StaleNoticeText = DescribeStaleness(result);
            StatusText = _allItems.Count == 0 && result.Error is null ? Loc.Tr("PluginStore_Empty") : null;
        }
        catch (Exception ex)
        {
            // The service reports what it expects as an error message; anything left is a surprise.
            // Saying so beats leaving the page on "loading plugins…" with nothing ever happening.
            Console.WriteLine($"[PluginStore] Loading the list failed unexpectedly: {ex}");
            NoticeText = Loc.Tr("PluginStore_CatalogUnavailable", ex.Message);
            StatusText = null;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Why the shown list may not be current, or null when it came fresh from the server.</summary>
    private static string DescribeStaleness(PluginStoreResult result)
    {
        if (result.IsOutdatedSchema)
        {
            return Loc.Tr("PluginStore_CatalogTooOld");
        }

        if (!result.IsFromCache)
        {
            return null;
        }

        return result.CachedAt is { } cachedAt
            ? Loc.Tr("PluginStore_CachedNotice", cachedAt.ToLocalTime().ToString("g"))
            : Loc.Tr("PluginStore_CachedNoticeNoTime");
    }

    private async Task InstallAsync(PluginStoreRowViewModel row)
    {
        PluginReleaseCandidate candidate = row?.Item.Available;
        if (candidate is null || row.IsBusy)
        {
            return;
        }

        // Release notes first; nothing is downloaded unless the user confirms. The notes are read while the
        // dialog is already open, so the decision never waits on a request.
        DialogResult confirmed = await dialogService.ShowDialogAsync<PluginReleaseNotesViewModel, DialogResult>(
            vm =>
            {
                vm.Initialize(row.Item);
                _ = vm.LoadNotesAsync();
            });
        if (confirmed is not { IsConfirmed: true })
        {
            return;
        }

        CancellationToken cancellation = row.BeginDownload();
        StatusText = Loc.Tr("PluginStore_Downloading", row.Name, candidate.Version);
        try
        {
            Progress<double> progress = new(value => row.Progress = value * 100);
            PluginDownloadResult download = await store.DownloadAsync(candidate, progress, cancellation);
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

            // A fresh install is not switched on: enabling is per device, so it is the user's
            // call, on the Plugins page, per device. Say so rather than leaving them to wonder
            // why nothing happened.
            if (row.Item.Installed is null)
            {
                StatusText = Loc.Tr("PluginStore_InstalledNeedsEnable", row.Name);
            }

            if (result.RequiresRestart)
            {
                store.MarkRestartRequired(row.Item.Entry.Id);
                ShowRestartHint = true;
            }

            PluginsChanged?.Invoke();
        }
        finally
        {
            row.EndDownload();
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

    /// <summary>An update is news, not a warning: it gets the info pill.</summary>
    public bool IsStatusInfo => Item.Status == PluginStoreStatus.UpdateAvailable;

    public bool IsStatusWarn => Item.Status is PluginStoreStatus.RestartRequired
        or PluginStoreStatus.RequiresNewerApp;

    public bool IsStatusNeutral => !IsStatusOk && !IsStatusWarn && !IsStatusInfo;

    /// <summary>Nothing here can be installed on this system; the tile steps back.</summary>
    public bool IsUnavailable => Item.Status is PluginStoreStatus.NoRelease or PluginStoreStatus.Unavailable;

    /// <summary>Author and platforms under the name; each part is optional.</summary>
    public string MetaText
    {
        get
        {
            List<string> parts = [];
            if (!string.IsNullOrWhiteSpace(Author))
                parts.Add(Author);
            if (!string.IsNullOrWhiteSpace(VersionText))
                parts.Add(VersionText);

            return string.Join("  ·  ", parts);
        }
    }

    /// <summary>A line explaining a status that is not self-explanatory.</summary>
    public string NoteText => Item.Status switch
    {
        PluginStoreStatus.ManuallyInstalled => Loc.Tr("PluginStore_AdoptNote"),
        PluginStoreStatus.RestartRequired => Loc.Tr("PluginStore_RestartNote"),
        PluginStoreStatus.RequiresNewerApp when Item.Newest is not null =>
            Loc.Tr("PluginStore_NewerNeedsApp", Item.Newest.Version),
        _ => null
    };

    public bool HasNote => NoteText != null;

    /// <summary>There is a release whose notes can be read.</summary>
    public bool HasReleaseNotes => Item.Available is not null || Item.Newest is not null;

    /// <summary>A hand-copied plugin the catalog knows can be taken over by the store.</summary>
    public bool CanAdopt => Item.Status == PluginStoreStatus.ManuallyInstalled && Item.Installed is not null;

    /// <summary>An installed plugin can be configured; the tile offers the jump.</summary>
    public bool CanSetup => Item.Installed is not null;

    /// <summary>Its newest release needs a newer LoupixDeck, so an app update is the way out.</summary>
    public bool CanCheckAppUpdate => Item.Status == PluginStoreStatus.RequiresNewerApp;

    /// <summary>The change is on disk but only takes effect on the next start.</summary>
    public bool CanRestart => Item.Status == PluginStoreStatus.RestartRequired;

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

    /// <summary>Cancels the download in flight. The service reports the cancellation as an
    /// ordinary failure, so the tile simply goes back to what it was.</summary>
    private CancellationTokenSource _download;

    public CancellationToken BeginDownload()
    {
        _download?.Dispose();
        _download = new CancellationTokenSource();
        IsBusy = true;
        Progress = 0;
        return _download.Token;
    }

    public void EndDownload()
    {
        IsBusy = false;
        _download?.Dispose();
        _download = null;
    }

    public void Cancel() => _download?.Cancel();

    public bool IsIdle => !IsBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    public partial double Progress { get; set; }

    /// <summary>Caption under the progress track.</summary>
    public string ProgressText => Loc.Tr("PluginStore_Downloading", Name,
        Item.Available?.Version?.ToString() ?? string.Empty);

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
