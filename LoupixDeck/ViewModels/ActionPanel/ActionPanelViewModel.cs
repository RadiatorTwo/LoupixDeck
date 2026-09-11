using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Models;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services.AppLauncher;
using LoupixDeck.Services.Commands;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;
using LoupixDeck.ViewModels.CommandPicker;

namespace LoupixDeck.ViewModels.ActionPanel;

/// <summary>
/// The main window's side panel: the applications installed on this machine, and every command the
/// deck can run. A row is dragged onto a button, or assigned to the selected button with a click.
/// </summary>
/// <remarks>
/// The actions half deliberately reads the same <see cref="MenuEntry"/> catalogue the command
/// picker does, built by <see cref="IMenuTreeBuilder"/>. That catalogue already carries core
/// commands, user macros, profile activation and each loaded plugin, with the icons and
/// descriptions they declare — a second hand-maintained action list would drift from it and would
/// never see a plugin at all. It is shown through the same <c>CommandPickerView</c> the button
/// editors use, so there is one command list to learn and no second projection to keep in step.
/// </remarks>
public partial class ActionPanelViewModel : ViewModelBase
{
    /// <summary>Decode width for an application row icon, matching the app picker's rows.</summary>
    private const int IconWidth = 48;

    /// <summary>mdi-puzzle — section glyph for a catalogue group that declares none.</summary>
    private const string GroupFallbackGlyph = "\U000F0431";

    private readonly IAppDiscoveryService _discovery;
    private readonly ICustomAppStore _customApps;
    private readonly IAppIconExtractor _icons;
    private readonly IMenuTreeBuilder _menuTreeBuilder;
    private readonly CancellationTokenSource _cancellation = new();

    // The live catalogue. Owned here rather than borrowed, because the panel outlives any one
    // dialog and has to keep seeing plugin groups arrive.
    private readonly ObservableCollection<MenuEntry> _catalogue = [];

    private readonly List<AppPanelItemViewModel> _allApps = [];

    private bool _loadStarted;

    public ActionPanelViewModel(IAppDiscoveryService discovery, ICustomAppStore customApps,
        IAppIconExtractor icons, IMenuTreeBuilder menuTreeBuilder)
    {
        _discovery = discovery;
        _customApps = customApps;
        _icons = icons;
        _menuTreeBuilder = menuTreeBuilder;
    }

    // ── Panel state ────────────────────────────────────────────────────────

    /// <summary>Whether the panel is showing.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    partial void OnIsOpenChanged(bool value)
    {
        // Normally a no-op: the lists are warmed in the background at start-up. It stays here so a
        // panel opened before that finished, or after it failed, still asks for its content.
        if (value)
            _ = EnsureLoadedAsync();
    }

    // ── Applications ───────────────────────────────────────────────────────

    /// <summary>The application rows currently shown, after the search filter.</summary>
    public ObservableCollection<PanelItemViewModel> Apps { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowApps))]
    public partial bool IsLoadingApps { get; set; } = true;

    /// <summary>Why the application list is empty, when that is worth saying — an unsupported
    /// platform, or a scan that found nothing. Shown instead of a blank list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowApps))]
    [NotifyPropertyChangedFor(nameof(HasAppsBlockReason))]
    public partial string AppsBlockReason { get; set; }

    public bool HasAppsBlockReason => !string.IsNullOrWhiteSpace(AppsBlockReason);

    public bool ShowApps => !IsLoadingApps && !HasAppsBlockReason;

    [ObservableProperty]
    public partial string AppCountLabel { get; set; } = string.Empty;

    public string AppSearchText
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
                ApplyAppFilter();
        }
    } = string.Empty;

    // ── Actions ────────────────────────────────────────────────────────────

    /// <summary>
    /// The command catalogue, shown through the same picker control the button editors use, over
    /// the same <see cref="MenuEntry"/> tree. Created lazily so it can reference the catalogue this
    /// panel owns.
    /// </summary>
    public CommandPickerViewModel CommandPicker => field ??= new CommandPickerViewModel(_catalogue);

    // ── Loading ────────────────────────────────────────────────────────────

    /// <summary>
    /// Fills both halves of the panel. Idempotent: the first call does the work and every later one
    /// returns immediately, so re-opening the panel does not rescan. Started in the background once
    /// the device is up, so the panel is already populated the first time it is opened.
    /// </summary>
    public async Task EnsureLoadedAsync()
    {
        if (_loadStarted)
            return;

        _loadStarted = true;

        // The catalogue is wanted for the whole session, so it is built even if the scan below
        // fails. Core groups land synchronously; plugin groups arrive later and rebuild the list.
        await _menuTreeBuilder.BuildInto(_catalogue, ButtonTargets.TouchButton);

        await LoadAppsAsync(rescan: false);
    }

    /// <summary>
    /// Scans again, for applications installed and macros or profiles created while the app was
    /// running. Neither list notices such a change on its own.
    /// </summary>
    public IAsyncRelayCommand RefreshCommand => field ??= Relay.Create(RefreshAsync, () => !IsRefreshing);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    public partial bool IsRefreshing { get; set; }

    private async Task RefreshAsync()
    {
        if (IsRefreshing)
            return;

        IsRefreshing = true;
        try
        {
            // Rebuilt rather than merged into: BuildInto merges groups, so building into the
            // populated collection would leave every entry twice over. The picker follows the
            // collection and reprojects itself.
            _catalogue.Clear();
            await _menuTreeBuilder.BuildInto(_catalogue, ButtonTargets.TouchButton);

            _loadStarted = true;
            await LoadAppsAsync(rescan: true);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>
    /// Adds a program the scan does not find — a portable build in an unindexed folder, a launcher
    /// script, a shortcut kept elsewhere. Stored separately from the scan, so it survives both a
    /// rescan and a restart.
    /// </summary>
    public IAsyncRelayCommand AddApplicationCommand => field ??= Relay.Create(AddApplicationAsync);

    private async Task AddApplicationAsync()
    {
        string path = await FileDialogHelper.OpenApplicationDialog();
        if (string.IsNullOrEmpty(path))
            return;

        InstalledApp added = _customApps.Add(path);
        if (added == null)
            return;

        AppPanelItemViewModel row = new(added) { CanRemove = true };
        _allApps.Insert(0, row);
        AppsBlockReason = null;
        ApplyAppFilter();

        // One icon, so it is not left blank until the next full scan.
        try
        {
            row.Icon = await _icons.GetThumbnailAsync(added, IconWidth, _cancellation.Token);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ActionPanel] Icon failed for '{added.Name}': {ex.Message}");
        }
    }

    /// <summary>Removes an application the user added. Offered only on those rows: a scanned
    /// application would simply come back on the next scan.</summary>
    public IRelayCommand<PanelItemViewModel> RemoveApplicationCommand
        => field ??= Relay.Create<PanelItemViewModel>(RemoveApplication);

    private void RemoveApplication(PanelItemViewModel row)
    {
        if (row is not AppPanelItemViewModel app || !_customApps.Contains(app.App))
            return;

        _customApps.Remove(app.App);
        _allApps.Remove(app);
        ApplyAppFilter();
    }

    private async Task LoadAppsAsync(bool rescan)
    {
        IsLoadingApps = true;
        AppsBlockReason = null;

        // A platform without discovery still lists whatever the user added by hand, so the panel is
        // useful there rather than merely explaining itself.
        if (!_discovery.IsSupported)
        {
            Rebuild([]);
            IsLoadingApps = false;

            if (_allApps.Count == 0)
                AppsBlockReason = "Listing installed applications is not supported on this system. "
                    + "Add a program with the + button.";
            else
                LoadIcons();

            return;
        }

        IReadOnlyList<InstalledApp> apps;
        try
        {
            apps = rescan
                ? await _discovery.RefreshAsync(_cancellation.Token)
                : await _discovery.GetAppsAsync(_cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            IsLoadingApps = false;
            AppsBlockReason = "Installed applications could not be listed.";
            Console.WriteLine($"[ActionPanel] Application scan failed: {ex.Message}");
            return;
        }

        Rebuild(apps);
        IsLoadingApps = false;

        if (_allApps.Count == 0)
        {
            AppsBlockReason = "No installed applications were found.";
            return;
        }

        LoadIcons();
    }

    /// <summary>
    /// Rebuilds the row list from a scan plus the applications the user added by hand. The added
    /// ones come first: they are there because the scan missed them, so burying them in a few
    /// hundred scanned rows would defeat the point.
    /// </summary>
    private void Rebuild(IReadOnlyList<InstalledApp> scanned)
    {
        _allApps.Clear();

        foreach (InstalledApp app in _customApps.Apps)
            _allApps.Add(new AppPanelItemViewModel(app) { CanRemove = true });

        foreach (InstalledApp app in scanned)
            _allApps.Add(new AppPanelItemViewModel(app));

        ApplyAppFilter();

        int games = _allApps.Count(row => row.App.IsGame);
        AppCountLabel = games > 0
            ? $"{_allApps.Count} applications, {games} games"
            : $"{_allApps.Count} applications";
    }

    /// <summary>
    /// Streams the application icons in on a background thread. Fire-and-forget on purpose: the
    /// rows are usable without icons and nothing downstream waits on them. Cancelled with the
    /// panel, so a closing window does not leave hundreds of extractions running.
    /// </summary>
    private void LoadIcons()
    {
        List<AppPanelItemViewModel> rows = [.. _allApps];
        CancellationToken token = _cancellation.Token;

        _ = Task.Run(async () =>
        {
            foreach (AppPanelItemViewModel row in rows)
            {
                if (token.IsCancellationRequested)
                    return;

                Bitmap icon;
                try
                {
                    icon = await _icons.GetThumbnailAsync(row.App, IconWidth, token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // One unreadable executable must not stop the remaining icons.
                    Console.WriteLine($"[ActionPanel] Icon failed for '{row.Title}': {ex.Message}");
                    continue;
                }

                if (icon == null || token.IsCancellationRequested)
                    continue;

                await Dispatcher.UIThread.InvokeAsync(() => row.Icon = icon);
            }
        }, token);
    }

    // ── Filtering ──────────────────────────────────────────────────────────

    private void ApplyAppFilter()
    {
        string search = AppSearchText?.Trim() ?? string.Empty;

        IEnumerable<AppPanelItemViewModel> filtered = search.Length == 0
            ? _allApps
            : _allApps.Where(row => Matches(row, search));

        Apps.Clear();
        foreach (AppPanelItemViewModel row in filtered)
            Apps.Add(row);
    }

    private static bool Matches(PanelItemViewModel row, string search) =>
        row.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
        (row.Subtitle?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>Stops the icon stream and detaches from the catalogue. Called when the device this
    /// panel belongs to goes away.</summary>
    public void Cleanup()
    {
        if (!_cancellation.IsCancellationRequested)
            _cancellation.Cancel();

        CommandPicker.Cleanup();
    }
}
