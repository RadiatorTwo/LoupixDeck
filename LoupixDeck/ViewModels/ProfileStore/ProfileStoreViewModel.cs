using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Commands;
using LoupixDeck.Localization;
using LoupixDeck.Models;
using LoupixDeck.Models.Portable;
using LoupixDeck.Registry;
using LoupixDeck.Services;
using LoupixDeck.Services.PluginStore;
using LoupixDeck.Services.Portable;
using LoupixDeck.Services.ProfileStore;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels.ProfileStore;

/// <summary>
/// The community profile store (issue #587): the shared profiles, workspaces and pages of the
/// LoupixDeck.Profiles repository. Read-only and one way: an entry is downloaded, verified and handed to
/// the regular import preview, which decides where it goes and what it needs.
/// </summary>
public sealed partial class ProfileStoreViewModel : DialogViewModelBase<DialogResult>, IAsyncInitViewModel
{
    private readonly IProfileStoreService _store;
    private readonly IDialogService _dialogService;
    private readonly ResolvedDevice _device;

    /// <summary>Lets the rows stop waiting for their previews once they are replaced or the window closes.</summary>
    private CancellationTokenSource _previewLoads;

    /// <summary>Every entry of the list, in list order.</summary>
    private readonly List<ProfileStoreRowViewModel> _allItems = [];

    public ProfileStoreViewModel(IProfileStoreService store, IDialogService dialogService, ResolvedDevice device)
    {
        _store = store;
        _dialogService = dialogService;
        _device = device;
        Items = [];
    }

    /// <summary>
    /// Shows the store. Returns true when at least one entry was imported, so the caller can redraw
    /// what lists profiles and workspaces.
    /// </summary>
    public static async Task<bool> ShowAsync(IDialogService dialogService)
    {
        ProfileStoreViewModel store = null;
        await dialogService.ShowDialogAsync<ProfileStoreViewModel, DialogResult>(vm => store = vm);
        return store?.ImportedAny == true;
    }

    public event Action CloseWindow;

    /// <summary>True once an entry was imported while the window was open.</summary>
    public bool ImportedAny { get; private set; }

    /// <summary>What the window shows: the entries narrowed by device, platform and search.</summary>
    public ObservableCollection<ProfileStoreRowViewModel> Items { get; }

    public string DeviceName => _device.Info.Name;

    [ObservableProperty]
    public partial string SearchText { get; set; }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    /// <summary>Off by default: a package from another device fits its keys and dials, not these.</summary>
    [ObservableProperty]
    public partial bool ShowAllDevices { get; set; }

    partial void OnShowAllDevicesChanged(bool value) => ApplyFilter();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    public partial bool IsImporting { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string StatusText { get; set; }

    public bool HasStatus => !string.IsNullOrEmpty(StatusText);

    /// <summary>Why the list could not be downloaded, and whether an older copy is shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    public partial string NoticeText { get; set; }

    public bool HasNotice => !string.IsNullOrEmpty(NoticeText);

    /// <summary>True when the list is loaded but nothing passes the filters.</summary>
    public bool HasNoMatches => !IsLoading && _allItems.Count > 0 && Items.Count == 0;

    /// <summary>True when filters hide entries; the hint then points at "Show all devices".</summary>
    public bool HasHiddenDeviceEntries => !ShowAllDevices && _allItems.Any(r => !r.MatchesDevice && r.MatchesSearch(SearchText));

    public IAsyncRelayCommand RefreshCommand => field ??= Relay.Create(() => LoadAsync(force: true),
        () => !IsLoading && !IsImporting);

    public IAsyncRelayCommand<ProfileStoreRowViewModel> ImportCommand =>
        field ??= Relay.Create<ProfileStoreRowViewModel>(ImportAsync, _ => !IsLoading && !IsImporting);

    public IRelayCommand<ProfileStoreRowViewModel> OpenPageCommand =>
        field ??= Relay.Create<ProfileStoreRowViewModel>(row =>
        {
            if (row is not null)
                OpenUrlCommand.TryOpen(row.Entry.FolderUrl);
        });

    public IRelayCommand OpenRepositoryCommand => field ??= Relay.Create(() =>
        OpenUrlCommand.TryOpen($"https://github.com/{ProfileStoreService.Repository}"));

    public IRelayCommand CloseCommand => field ??= Relay.Create(() =>
    {
        Cancel();
        CloseWindow?.Invoke();
    });

    public Task InitializeAsync() => LoadAsync(force: false);

    /// <summary>Stops the preview downloads the rows still wait for. Called when the window closes.</summary>
    public void OnClosed() => _previewLoads?.Cancel();

    private async Task LoadAsync(bool force)
    {
        IsLoading = true;
        StatusText = null;
        NoticeText = null;
        try
        {
            ProfileStoreResult result = await _store.GetEntriesAsync(force);

            if (result.Error is not null)
            {
                NoticeText = !result.IsFromCache
                    ? result.Error
                    : result.CacheTime is { } time
                        ? Loc.Tr("ProfileStore_CachedNotice", time.ToLocalTime().ToString("g"))
                        : Loc.Tr("ProfileStore_CachedNoticeNoTime");
            }

            _previewLoads?.Cancel();
            _previewLoads = new CancellationTokenSource();

            // Models with the same keys, dials and strips share packages: the Loupedeck Live and the
            // Razer Stream Controller are the same hardware under two names.
            HashSet<string> compatibleSlugs = DeviceRegistry.SupportedDevices
                .Where(d => d.Geometry == _device.Info.Geometry)
                .Select(d => d.Slug)
                .Append(_device.Slug)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            _allItems.Clear();
            _allItems.AddRange(result.Entries
                .Select(e => new ProfileStoreRowViewModel(e, compatibleSlugs))
                .OrderByDescending(r => r.MatchesDevice)
                .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase));

            foreach (ProfileStoreRowViewModel row in _allItems)
                _ = row.LoadPreviewAsync(_previewLoads.Token);

            if (_allItems.Count == 0 && result.Error is null)
                StatusText = Loc.Tr("ProfileStore_Empty");
        }
        finally
        {
            IsLoading = false;
            ApplyFilter();
        }
    }

    private void ApplyFilter()
    {
        Items.Clear();
        foreach (ProfileStoreRowViewModel row in _allItems)
        {
            if ((ShowAllDevices || row.MatchesDevice) && row.MatchesSearch(SearchText))
                Items.Add(row);
        }

        OnPropertyChanged(nameof(HasNoMatches));
        OnPropertyChanged(nameof(HasHiddenDeviceEntries));
    }

    private async Task ImportAsync(ProfileStoreRowViewModel row)
    {
        if (row is null)
            return;

        IsImporting = true;
        row.IsBusy = true;
        StatusText = Loc.Tr("ProfileStore_Downloading", row.Name);
        string packagePath = null;
        try
        {
            ProfileDownloadResult download = await _store.DownloadAsync(row.Entry);
            if (!download.Success)
            {
                StatusText = download.Error;
                return;
            }

            packagePath = download.PackagePath;
            StatusText = null;

            ProfilePackageResult result = await ProfileImportViewModel.ShowAsync(_dialogService, packagePath);
            if (result is null)
                return;

            if (result.Success)
                ImportedAny = true;

            // Same report as the header's import: a clean import just shows up in the selectors.
            if (result.Success && result.Warnings.Count == 0 && result.LayoutTemplate is not { ButtonsChanged: > 0 })
            {
                StatusText = Loc.Tr("ProfileStore_Imported", row.Name);
                return;
            }

            string message = string.Join(Environment.NewLine, result.ReportLines());
            await _dialogService.ShowDialogAsync<ConfirmDialogViewModel, DialogResult>(vm =>
                vm.Configure(message, title: Loc.Tr("MainWindow_ImportPackageResultTitle"),
                    confirmText: Loc.Tr("Confirm_Ok"), showCancel: false));
        }
        finally
        {
            _store.DeleteDownload(packagePath);
            row.IsBusy = false;
            IsImporting = false;
        }
    }
}

/// <summary>One entry of the community profile list.</summary>
public sealed partial class ProfileStoreRowViewModel : ObservableObject
{
    /// <param name="compatibleSlugs">Slugs of the models whose packages fit this device.</param>
    public ProfileStoreRowViewModel(ProfileCatalogEntry entry, IReadOnlySet<string> compatibleSlugs)
    {
        Entry = entry;
        MatchesDevice = entry.Device?.Slug is { } slug && compatibleSlugs.Contains(slug)
                        && entry.SupportsCurrentPlatform();
    }

    public ProfileCatalogEntry Entry { get; }

    public string Name => Entry.DisplayName;

    public string Description => Entry.Description;

    /// <summary>"by Author · 1.0.0".</summary>
    public string MetaText => string.Join(" · ", new[]
    {
        string.IsNullOrWhiteSpace(Entry.Author) ? null : Loc.Tr("PluginStore_ByAuthor", Entry.Author),
        Entry.Version
    }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public string KindText => Entry.Kind switch
    {
        nameof(PackageKind.Profile) => Loc.Tr("ProfileStore_KindProfile"),
        nameof(PackageKind.Workspace) => Loc.Tr("ProfileStore_KindWorkspace"),
        nameof(PackageKind.TouchPage) => Loc.Tr("ProfileStore_KindTouchPage"),
        nameof(PackageKind.RotaryPage) => Loc.Tr("ProfileStore_KindRotaryPage"),
        _ => Entry.Kind ?? string.Empty
    };

    /// <summary>"Loupedeck Live · Windows, Linux".</summary>
    public string DeviceText
    {
        get
        {
            string device = Entry.Device?.Name ?? Entry.Device?.Slug ?? Loc.Tr("ProfileStore_UnknownDevice");
            string platforms = Entry.Platforms is { Count: > 0 }
                ? string.Join(", ", Entry.Platforms)
                : Loc.Tr("ProfileStore_AllPlatforms");
            return $"{device} · {platforms}";
        }
    }

    public string TagsText => Entry.Tags is { Count: > 0 } tags ? string.Join("  ", tags.Select(t => "#" + t)) : string.Empty;

    public bool HasTags => TagsText.Length > 0;

    public string PluginsText => Entry.RequiredPlugins is { Count: > 0 } plugins
        ? Loc.Tr("ProfileStore_NeedsPlugins", string.Join(", ", plugins.Select(p => p.Name ?? p.Id)))
        : string.Empty;

    public bool HasPlugins => PluginsText.Length > 0;

    public string LicenseText => Loc.Tr("ProfileStore_License",
        string.IsNullOrWhiteSpace(Entry.License) ? "CC-BY-4.0" : Entry.License);

    /// <summary>True when the entry was made on this model (or one with the same layout) and for this system.</summary>
    public bool MatchesDevice { get; }

    /// <summary>Why the entry may not fit; only shown with "Show all devices".</summary>
    public string MismatchText => MatchesDevice
        ? string.Empty
        : Entry.SupportsCurrentPlatform()
            ? Loc.Tr("ProfileStore_OtherDevice")
            : Loc.Tr("ProfileStore_OtherPlatform");

    public bool HasMismatch => !MatchesDevice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsBusy { get; set; }

    public bool IsIdle => !IsBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    public partial Bitmap Preview { get; set; }

    public bool HasPreview => Preview is not null;

    /// <summary>Case-insensitive match on name, description, author and tags.</summary>
    public bool MatchesSearch(string search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return true;

        string term = search.Trim();
        return Contains(Entry.Name) || Contains(Entry.Description) || Contains(Entry.Author)
               || Entry.Tags?.Any(Contains) == true;

        bool Contains(string value) => value?.Contains(term, StringComparison.CurrentCultureIgnoreCase) == true;
    }

    public async Task LoadPreviewAsync(CancellationToken cancellation)
    {
        string url = Entry.Preview;
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out Uri uri)
                                           || uri.Scheme != Uri.UriSchemeHttps)
        {
            return;
        }

        try
        {
            // Same in-session image cache as the plugin icons: a refresh does not fetch every preview again.
            Preview = await PluginStoreIconCache.GetAsync(url, cancellation);
        }
        catch (OperationCanceledException)
        {
            // The row is gone; the download carries on and stays in the cache.
        }
    }
}
