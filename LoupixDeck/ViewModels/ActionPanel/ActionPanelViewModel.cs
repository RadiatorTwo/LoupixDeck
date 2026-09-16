using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Localization;
using LoupixDeck.Models;
using LoupixDeck.PluginSdk;
using LoupixDeck.Registry;
using LoupixDeck.Services;
using LoupixDeck.Services.AppLauncher;
using LoupixDeck.Services.AppSwitching;
using LoupixDeck.Services.Commands;
using LoupixDeck.Services.Companion;
using LoupixDeck.Services.DialPresets;
using LoupixDeck.Services.Macros;
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
    private readonly IDialPresetCatalog _dialPresets;
    private readonly ICompanionCoordinator _companions;
    private readonly IConfigService _configService;
    private readonly IMacroManager _macros;
    private readonly ICommandRegistry _commandRegistry;
    private readonly LoupedeckConfig _config;
    private readonly ResolvedDevice _device;
    private readonly CancellationTokenSource _cancellation = new();

    // Coalesces the change notifications that can invalidate the catalogue: a save or a group edit
    // often arrives as a burst, and one rebuild after it settles is enough.
    private readonly DispatcherTimer _catalogueRefreshTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

    // What the catalogue was last built from, so a save that touched nothing it lists (a button
    // edit, a page switch) does not rebuild it, and the plugin groups do not reload for nothing.
    private string _catalogueSignature;
    private bool _catalogueRefreshForced;
    private bool _catalogueBuilding;
    private bool _catalogueRefreshPending;

    // The live catalogue. Owned here rather than borrowed, because the panel outlives any one
    // dialog and has to keep seeing plugin groups arrive.
    private readonly ObservableCollection<MenuEntry> _catalogue = [];

    private readonly List<AppPanelItemViewModel> _allApps = [];

    private bool _loadStarted;

    public ActionPanelViewModel(IAppDiscoveryService discovery, ICustomAppStore customApps,
        IAppIconExtractor icons, IMenuTreeBuilder menuTreeBuilder, IDialPresetCatalog dialPresets,
        ICompanionCoordinator companions, IConfigService configService, IMacroManager macros,
        ICommandRegistry commandRegistry, LoupedeckConfig config, ResolvedDevice device)
    {
        _commandRegistry = commandRegistry;
        _discovery = discovery;
        _customApps = customApps;
        _icons = icons;
        _menuTreeBuilder = menuTreeBuilder;
        _dialPresets = dialPresets;
        _companions = companions;
        _configService = configService;
        _macros = macros;
        _config = config;
        _device = device;

        _dialPresets.PresetsChanged += OnDialPresetsChanged;
        RebuildDialPresets();

        LocalizationManager.Instance.PropertyChanged += OnLanguageChanged;

        _catalogueRefreshTimer.Tick += OnCatalogueRefreshTick;
        _companions.GroupsChanged += OnCatalogueSourceChanged;
        _companions.DeviceOnlineStateChanged += OnDeviceOnlineStateChanged;
        _configService.ConfigSaved += OnConfigSaved;
        _macros.MacrosChanged += OnMacrosChanged;
        _commandRegistry.CommandsChanged += OnCommandsChanged;
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
        {
            _ = EnsureLoadedAsync();
            RequestCatalogueRefresh();
        }
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

    // ── Dial presets ───────────────────────────────────────────────────────

    /// <summary>
    /// The dial presets, as rows that can be dragged onto a dial or assigned to a selected one.
    /// Their own section rather than part of the command list above, because a preset is offered on
    /// rotary encoders alone and that list is built for touch buttons.
    /// </summary>
    public ObservableCollection<PanelItemViewModel> DialPresets { get; } = [];

    /// <summary>
    /// Opens the rename and delete dialogs. Set by the device view model, which owns the dialog
    /// service; the panel itself is a list and has no business showing modal windows.
    /// </summary>
    public Func<DialPreset, Task> RenamePreset { get; set; }

    public Func<DialPreset, Task> DeletePreset { get; set; }

    /// <summary>Renames a user preset. Offered on the row's context menu.</summary>
    public IRelayCommand<PanelItemViewModel> RenamePresetCommand
        => field ??= Relay.Create<PanelItemViewModel>(row => Invoke(RenamePreset, row));

    /// <summary>Deletes a user preset, after the device view model confirms.</summary>
    public IRelayCommand<PanelItemViewModel> DeletePresetCommand
        => field ??= Relay.Create<PanelItemViewModel>(row => Invoke(DeletePreset, row));

    private static void Invoke(Func<DialPreset, Task> action, PanelItemViewModel row)
    {
        if (action == null || row is not DialPresetPanelItemViewModel { Preset.IsReadOnly: false } preset)
            return;

        _ = action(preset.Preset);
    }

    // ── Profile links ──────────────────────────────────────────────────────

    /// <summary>
    /// Link and unlink an application to the active profile. Set by the device view model, which
    /// owns the profile menu and its dialogs; the panel only knows the row that was right-clicked.
    /// </summary>
    public Func<InstalledApp, Task> LinkToProfile { get; set; }

    public Func<InstalledApp, Task> UnlinkFromProfile { get; set; }

    /// <summary>Links an application row to the active profile. Offered on the row's context menu.</summary>
    public IRelayCommand<PanelItemViewModel> LinkToProfileCommand
        => field ??= Relay.Create<PanelItemViewModel>(row => InvokeApp(LinkToProfile, row));

    /// <summary>Removes the active profile's link to an application row, after the flow confirms.</summary>
    public IRelayCommand<PanelItemViewModel> UnlinkFromProfileCommand
        => field ??= Relay.Create<PanelItemViewModel>(row => InvokeApp(UnlinkFromProfile, row));

    /// <summary>Menu header naming the active profile, e.g. "Open 'Gaming' with this application".</summary>
    [ObservableProperty]
    public partial string LinkToProfileHeader { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string UnlinkFromProfileHeader { get; set; } = string.Empty;

    // Last state passed to RefreshProfileLinks, applied to rows created after it.
    private string _profileName = string.Empty;
    private string _linkedProcessName = string.Empty;
    private bool _linkingSupported;

    private static void InvokeApp(Func<InstalledApp, Task> action, PanelItemViewModel row)
    {
        if (action == null || row is not AppPanelItemViewModel app)
            return;

        _ = action(app.App);
    }

    /// <summary>
    /// Re-evaluates every application row against the active profile's link and rebuilds the menu
    /// headers. Called when the active profile, its link or its name changes.
    /// </summary>
    public void RefreshProfileLinks(string profileName, string linkedProcessName, bool linkingSupported)
    {
        _profileName = profileName ?? string.Empty;
        _linkedProcessName = linkedProcessName ?? string.Empty;
        _linkingSupported = linkingSupported;

        UpdateProfileLinkHeaders();

        foreach (AppPanelItemViewModel row in _allApps)
            ApplyProfileLink(row);
    }

    private void UpdateProfileLinkHeaders()
    {
        LinkToProfileHeader = Loc.Tr("ActionPanel_LinkToProfile", _profileName);
        UnlinkFromProfileHeader = Loc.Tr("ActionPanel_UnlinkFromProfile", _profileName);
    }

    private void ApplyProfileLink(AppPanelItemViewModel row)
    {
        row.IsLinkingSupported = _linkingSupported;
        row.IsLinkedToActiveProfile = _linkedProcessName.Length > 0
            && string.Equals(ContextRuleMatcher.Normalize(row.App.ProcessName), _linkedProcessName,
                StringComparison.OrdinalIgnoreCase);
    }

    private void OnLanguageChanged(object sender, PropertyChangedEventArgs e) =>
        Dispatcher.UIThread.Post(UpdateProfileLinkHeaders);

    private void OnDialPresetsChanged(object sender, EventArgs e) =>
        Dispatcher.UIThread.Post(RebuildDialPresets);

    private void RebuildDialPresets()
    {
        DialPresets.Clear();
        foreach (DialPreset preset in _dialPresets.Presets)
            DialPresets.Add(new DialPresetPanelItemViewModel(preset));
    }

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
        await RebuildCatalogueAsync();

        await LoadAppsAsync(rescan: false);
    }

    // ── Catalogue refresh ──────────────────────────────────────────────────
    //
    // The catalogue lists this device's profiles and workspaces and the user macros. None of those tell the menu builder when they
    // change, so the panel watches the saves and events behind them and rebuilds itself.

    /// <summary>
    /// Rebuilds the command catalogue shortly, if what it lists has changed since it was built.
    /// Safe to call from any thread and as often as wanted; bursts collapse into one check. Called
    /// on the panel's own triggers and by the main window when this device becomes the shown one.
    /// </summary>
    public void RequestCatalogueRefresh() => ScheduleCatalogueRefresh(force: false);

    private void ScheduleCatalogueRefresh(bool force)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // Before the first build there is nothing stale; that build reads the current state.
            if (!_loadStarted || _cancellation.IsCancellationRequested)
                return;

            _catalogueRefreshForced |= force;
            _catalogueRefreshTimer.Stop();
            _catalogueRefreshTimer.Start();
        });
    }

    private void OnCatalogueSourceChanged() => ScheduleCatalogueRefresh(force: false);

    private void OnConfigSaved(string filePath) => ScheduleCatalogueRefresh(force: false);

    // A master lists its companions as connected or offline.
    private void OnDeviceOnlineStateChanged(string deviceKey) => ScheduleCatalogueRefresh(force: false);

    // Macro names are not part of the signature, so a macro change always rebuilds.
    private void OnMacrosChanged(object sender, EventArgs e) => ScheduleCatalogueRefresh(force: true);

    // Enabling, disabling, installing or removing a plugin changes which commands exist. The
    // signature describes this device's profiles and pages, not the command set, so it would not
    // notice - the rebuild has to be forced or a freshly enabled plugin stays missing from the
    // panel until the next restart.
    private void OnCommandsChanged() => ScheduleCatalogueRefresh(force: true);

    private async void OnCatalogueRefreshTick(object sender, EventArgs e)
    {
        _catalogueRefreshTimer.Stop();

        bool force = _catalogueRefreshForced;
        _catalogueRefreshForced = false;

        try
        {
            if (!force && string.Equals(CatalogueSignature(), _catalogueSignature, StringComparison.Ordinal))
                return;

            await RebuildCatalogueAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ActionPanel] Catalogue refresh failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Clears and rebuilds the catalogue. A request that arrives while a build runs is not dropped:
    /// it runs once more afterwards, so the last change is always reflected.
    /// </summary>
    private async Task RebuildCatalogueAsync()
    {
        if (_catalogueBuilding)
        {
            _catalogueRefreshPending = true;
            return;
        }

        _catalogueBuilding = true;
        try
        {
            do
            {
                _catalogueRefreshPending = false;
                _catalogueSignature = CatalogueSignature();

                // Rebuilt rather than merged into: BuildInto merges groups, so building into the
                // populated collection would leave every entry twice over. The picker follows the
                // collection and reprojects itself.
                _catalogue.Clear();
                await _menuTreeBuilder.BuildInto(_catalogue, ButtonTargets.TouchButton);
            }
            while (_catalogueRefreshPending);
        }
        finally
        {
            _catalogueBuilding = false;
        }
    }

    /// <summary>
    /// A fingerprint of the state the catalogue's device-dependent groups are built from: this
    /// device's role and profile tree, and on a master each companion's connection state and pages.
    /// </summary>
    private string CatalogueSignature()
    {
        StringBuilder signature = new();
        signature.Append(_companions.IsFollowingMaster(_device.ScopeKey)).Append('|');
        AppendProfileTree(signature, _config);

        if (_companions.IsMaster(_device.ScopeKey))
        {
            foreach (string companionKey in _companions.GetCompanionKeys(_device.ScopeKey))
            {
                signature.Append("|C:").Append(companionKey).Append(':').Append(_companions.IsOnline(companionKey)).Append(';');
                LoupedeckConfig companion = _companions.GetDeviceConfig(companionKey);
                AppendProfileTree(signature, companion);
                AppendPages(signature, companion);
            }
        }

        return signature.ToString();
    }

    private static void AppendPages(StringBuilder signature, LoupedeckConfig config)
    {
        if (config?.Profiles == null)
            return;

        foreach (Workspace workspace in config.Profiles.Where(p => p.Workspaces != null).SelectMany(p => p.Workspaces))
        {
            IEnumerable<ButtonPageBase> pages = Enumerable.Empty<ButtonPageBase>()
                .Concat(workspace.TouchButtonPages ?? [])
                .Concat(workspace.RotaryButtonPages ?? [])
                .Concat(workspace.LeftRotaryButtonPages ?? [])
                .Concat(workspace.RightRotaryButtonPages ?? []);
            foreach (ButtonPageBase page in pages)
                signature.Append("G:").Append(page.Id).Append(':').Append(page.Name).Append(';');
        }
    }

    private static void AppendProfileTree(StringBuilder signature, LoupedeckConfig config)
    {
        if (config?.Profiles == null)
            return;

        foreach (Profile profile in config.Profiles)
        {
            signature.Append("P:").Append(profile.Id).Append(':').Append(profile.Name).Append(';');
            if (profile.Workspaces == null)
                continue;

            foreach (Workspace workspace in profile.Workspaces)
                signature.Append("W:").Append(workspace.Id).Append(':').Append(workspace.Name).Append(';');
        }
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
            _loadStarted = true;
            await RebuildCatalogueAsync();

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
        ApplyProfileLink(row);
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

        foreach (AppPanelItemViewModel row in _allApps)
            ApplyProfileLink(row);

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

        _dialPresets.PresetsChanged -= OnDialPresetsChanged;
        LocalizationManager.Instance.PropertyChanged -= OnLanguageChanged;

        _catalogueRefreshTimer.Stop();
        _catalogueRefreshTimer.Tick -= OnCatalogueRefreshTick;
        _companions.GroupsChanged -= OnCatalogueSourceChanged;
        _companions.DeviceOnlineStateChanged -= OnDeviceOnlineStateChanged;
        _configService.ConfigSaved -= OnConfigSaved;
        _macros.MacrosChanged -= OnMacrosChanged;
        _commandRegistry.CommandsChanged -= OnCommandsChanged;

        CommandPicker.Cleanup();
    }
}
