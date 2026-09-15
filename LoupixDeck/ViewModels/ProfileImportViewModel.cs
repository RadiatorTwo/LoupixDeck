using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Localization;
using LoupixDeck.Models;
using LoupixDeck.Models.Portable;
using LoupixDeck.Registry;
using LoupixDeck.Services;
using LoupixDeck.Services.Companion;
using LoupixDeck.Services.Portable;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels;

/// <summary>
/// Import preview for a <c>.loupixprofile</c> package (issue #133): shows what the package
/// contains and what it would mean here — device capability differences, required plugins,
/// unresolvable commands, macro conflicts — and collects the import decisions.
/// </summary>
/// <remarks>
/// Nothing outside the package's staging folder is touched until the user presses Import; both
/// Cancel and closing the window discard the analysis, so a staging folder never leaks.
/// </remarks>
public sealed partial class ProfileImportViewModel : DialogViewModelBase<DialogResult>, IAsyncInitViewModel
{
    private readonly IProfilePackageService _packageService;
    private readonly LoupedeckConfig _config;
    private readonly ICompanionCoordinator _companions;
    private readonly ResolvedDevice _device;

    private string _packagePath;
    private ProfilePackageAnalysis _analysis;

    public ProfileImportViewModel(IProfilePackageService packageService, LoupedeckConfig config,
        ICompanionCoordinator companions, ResolvedDevice device)
    {
        _packageService = packageService;
        _config = config;
        _companions = companions;
        _device = device;

        // Assigned here rather than inline so the generated setters run (see the collection-init
        // gotcha documented on LoupedeckConfig / Workspace).
        Plugins = new();
        Macros = new();
        UnknownCommands = new();
        Warnings = new();
        ImportTargets = new();
        ReplaceTargets = new();
        CompanionParts = new();
    }

    /// <summary>
    /// Asks for a package file, shows the import preview for it and returns the import result, or
    /// null when the user cancelled either step. Shared by every place that offers an import.
    /// </summary>
    public static async Task<ProfilePackageResult> ShowAsync(IDialogService dialogService)
    {
        string source = await FileDialogHelper.OpenProfilePackageDialog(WindowHelper.GetActiveWindow());
        if (string.IsNullOrEmpty(source))
            return null;

        ProfileImportViewModel importViewModel = null;
        DialogResult dialogResult = await dialogService.ShowDialogAsync<ProfileImportViewModel, DialogResult>(vm =>
        {
            importViewModel = vm;
            vm.Configure(source);
        });

        return dialogResult?.IsConfirmed == true ? importViewModel?.Result : null;
    }

    /// <summary>Package to inspect. Set by the caller before the dialog is shown.</summary>
    public void Configure(string packagePath) => _packagePath = packagePath;

    /// <summary>Raised when the dialog should close (after the result is set).</summary>
    public event Action CloseWindow;

    /// <summary>Set when the user confirmed and the import ran; null when cancelled.</summary>
    public ProfilePackageResult Result { get; private set; }

    // ───────── Loading / blocking state ─────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowContent))]
    public partial bool IsLoading { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowContent))]
    [NotifyPropertyChangedFor(nameof(HasBlockReason))]
    public partial string BlockReason { get; set; }

    public bool HasBlockReason => !string.IsNullOrWhiteSpace(BlockReason);

    public bool ShowContent => !IsLoading && !HasBlockReason;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    public partial bool IsImporting { get; set; }

    // ───────── Header ─────────

    [ObservableProperty]
    public partial string KindText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    public partial string NewName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDescription))]
    public partial string ProvenanceText { get; set; } = string.Empty;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    // ───────── Device mismatch ─────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDeviceMismatch))]
    public partial string DeviceMismatchMessage { get; set; }

    public bool HasDeviceMismatch => !string.IsNullOrWhiteSpace(DeviceMismatchMessage);

    // ───────── Contents ─────────

    public ObservableCollection<PackagePluginRow> Plugins { get; }
    public ObservableCollection<MacroConflictRow> Macros { get; }
    public ObservableCollection<string> UnknownCommands { get; }
    public ObservableCollection<string> Warnings { get; }

    public bool HasPlugins => Plugins.Count > 0;
    public bool HasMacros => Macros.Count > 0;
    public bool HasUnknownCommands => UnknownCommands.Count > 0;
    public bool HasWarnings => Warnings.Count > 0;

    /// <summary>True when at least one required plugin is installed but disabled for this device.</summary>
    public bool HasDisabledPlugins => Plugins.Any(p => p.IsDisabled);

    // ───────── Mode / targets ─────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReplace))]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    [NotifyPropertyChangedFor(nameof(CompanionReplaceWarning))]
    [NotifyPropertyChangedFor(nameof(HasCompanionReplaceWarning))]
    public partial bool ReplaceExisting { get; set; }

    public bool IsReplace => ReplaceExisting;

    [ObservableProperty]
    public partial bool BackupReplacedItem { get; set; } = true;

    /// <summary>Containers the item can be imported into (a profile for a workspace, a workspace
    /// for a page). Empty — and hidden — for a profile package.</summary>
    public ObservableCollection<ImportTargetRow> ImportTargets { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImportTargets))]
    public partial ImportTargetRow SelectedImportTarget { get; set; }

    public bool HasImportTargets => ImportTargets.Count > 0;

    /// <summary>Existing items the package can replace.</summary>
    public ObservableCollection<ImportTargetRow> ReplaceTargets { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    [NotifyPropertyChangedFor(nameof(CompanionReplaceWarning))]
    [NotifyPropertyChangedFor(nameof(HasCompanionReplaceWarning))]
    public partial ImportTargetRow SelectedReplaceTarget { get; set; }

    /// <summary>
    /// On a master, replacing a profile gives its workspaces new ids. The companions' mirrors move to
    /// the incoming workspaces matched to the old ones (<see cref="ReplacedWorkspaceMatcher"/>); a
    /// mirror of an old workspace without a counterpart is dropped with the companion's own pages
    /// inside. Names the companions that would lose pages; null when none would (or the package does
    /// not replace a profile).
    /// </summary>
    public string CompanionReplaceWarning
    {
        get
        {
            if (!IsReplace || _analysis?.Manifest?.Kind != PackageKind.Profile ||
                SelectedReplaceTarget?.ProfileId is not { } profileId)
                return null;

            Profile replaced = _config.Profiles?.FirstOrDefault(p => p.Id == profileId);
            if (replaced == null)
                return null;

            Dictionary<Guid, int> matched = ReplacedWorkspaceMatcher.Match([.. replaced.Workspaces ?? []],
                [.. _analysis.Payload?.Profile?.Workspaces ?? []]);
            List<Guid> unmatched = (replaced.Workspaces ?? []).Select(w => w.Id).Where(id => !matched.ContainsKey(id)).ToList();

            // A companion that receives its pages from the package gets them back with it.
            List<string> receiving = [.. CompanionParts.Select(row => row.SelectedTarget?.Key).Where(key => key != null)];

            IReadOnlyList<CompanionLossEntry> losses = CompanionImpact.ForWorkspaces(_companions, _device.ScopeKey, unmatched, receiving);
            return losses.Count == 0
                ? null
                : Loc.Tr("ProfileImport_ReplaceLosesCompanionPages") + Environment.NewLine + CompanionImpact.Describe(losses);
        }
    }

    public bool HasCompanionReplaceWarning => CompanionReplaceWarning != null;

    // ───────── Companion pages ─────────

    /// <summary>The companions whose own pages the package carries, each with the companion of this
    /// master that receives them. Empty unless this device is a master and the package has parts.</summary>
    public ObservableCollection<CompanionPartRow> CompanionParts { get; }

    public bool HasCompanionParts => CompanionParts.Count > 0;

    /// <summary>True when the package carries companion pages this device cannot take (it leads no group).</summary>
    [ObservableProperty]
    public partial bool CompanionPartsIgnored { get; set; }

    /// <summary>Label of the container list, e.g. "Import into profile".</summary>
    [ObservableProperty]
    public partial string ImportTargetLabel { get; set; } = string.Empty;

    public bool CanImport =>
        _analysis is { IsImportable: true } &&
        !IsImporting &&
        !string.IsNullOrWhiteSpace(NewName) &&
        Macros.All(m => m.IsValid) &&
        (!IsReplace || SelectedReplaceTarget != null);

    // ───────── Commands ─────────

    public IAsyncRelayCommand ImportCommand => field ??= Relay.Create(ImportAsync, () => CanImport);

    /// <summary>
    /// <see cref="ImportCommand"/> is a hand-rolled command, so its CanExecute is only re-evaluated
    /// when it is explicitly told to. Raising <see cref="CanImport"/> alone would leave the button
    /// stuck on the very first evaluation — made while the package was still being inspected and
    /// nothing was importable yet. Hooking the notification here covers every source at once: the
    /// generated [NotifyPropertyChangedFor] setters, the analysis result, and the macro rows.
    /// </summary>
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName == nameof(CanImport))
            ImportCommand.NotifyCanExecuteChanged();
    }

    public IRelayCommand CancelCommand => field ??= Relay.Create(() =>
    {
        DiscardAnalysis();
        Cancel();
        CloseWindow?.Invoke();
    });

    /// <summary>Drops the staged package. Called on cancel and from the window's Closing handler.</summary>
    public void DiscardAnalysis()
    {
        if (_analysis == null) return;

        _packageService.DiscardAnalysis(_analysis);
        _analysis = null;
    }

    public async Task InitializeAsync()
    {
        _analysis = await _packageService.InspectAsync(_packagePath);
        IsLoading = false;

        if (!_analysis.IsImportable)
        {
            BlockReason = _analysis.BlockReason;
            OnPropertyChanged(nameof(CanImport));
            return;
        }

        ProfilePackageManifest manifest = _analysis.Manifest;

        KindText = manifest.Kind switch
        {
            PackageKind.Profile => "Profile",
            PackageKind.Workspace => "Workspace",
            PackageKind.TouchPage => "Touch page",
            PackageKind.RotaryPage => "Rotary page",
            _ => manifest.Kind.ToString()
        };

        NewName = DisambiguateName(manifest.Name, manifest.Kind);
        Description = manifest.Description ?? string.Empty;
        ProvenanceText = BuildProvenance(manifest);
        DeviceMismatchMessage = _analysis.DeviceMismatch ? _analysis.DeviceMismatchMessage : null;

        foreach (PackagePluginStatus status in _analysis.Plugins)
            Plugins.Add(new PackagePluginRow(status));

        foreach (PackageMacroStatus macro in _analysis.ConflictingMacros)
        {
            MacroConflictRow row = new(macro.Name);
            row.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CanImport));
            Macros.Add(row);
        }

        foreach (string command in _analysis.UnknownCommands)
            UnknownCommands.Add(command);

        foreach (string warning in _analysis.Warnings)
            Warnings.Add(warning);

        BuildTargets(manifest.Kind);
        BuildCompanionParts(manifest.Kind);

        OnPropertyChanged(nameof(HasPlugins));
        OnPropertyChanged(nameof(HasMacros));
        OnPropertyChanged(nameof(HasUnknownCommands));
        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(HasDisabledPlugins));
        OnPropertyChanged(nameof(HasImportTargets));
        OnPropertyChanged(nameof(HasCompanionParts));
        OnPropertyChanged(nameof(CompanionReplaceWarning));
        OnPropertyChanged(nameof(HasCompanionReplaceWarning));
        OnPropertyChanged(nameof(CanImport));
    }

    /// <summary>
    /// One row per companion part of a profile or workspace package. Each offers this master's
    /// companions; the same companion (same key, i.e. the same machine) is preselected, else the only
    /// companion of the same model not taken yet, else nothing is imported for the part.
    /// </summary>
    private void BuildCompanionParts(PackageKind kind)
    {
        List<CompanionPackagePart> parts = [.. (_analysis.Payload?.Companions ?? []).Where(p => p?.DeviceKey != null)];
        if (parts.Count == 0 || kind is not (PackageKind.Profile or PackageKind.Workspace))
            return;

        if (!_companions.IsMaster(_device.ScopeKey))
        {
            CompanionPartsIgnored = true;
            return;
        }

        IReadOnlyList<string> companionKeys = _companions.GetCompanionKeys(_device.ScopeKey);
        CompanionTargetOption skip = new(null, Loc.Tr("ProfileImport_DoNotImport"));
        List<CompanionTargetOption> options =
        [
            skip,
            .. companionKeys.Select(key => new CompanionTargetOption(key, _companions.GetDisplayName(key)))
        ];

        HashSet<string> taken = new(StringComparer.OrdinalIgnoreCase);
        foreach (CompanionPackagePart part in parts)
        {
            CompanionTargetOption preselected =
                options.FirstOrDefault(o => o.Key != null && string.Equals(o.Key, part.DeviceKey, StringComparison.OrdinalIgnoreCase));

            if (preselected == null && !string.IsNullOrEmpty(part.DeviceSlug))
            {
                List<CompanionTargetOption> sameModel = options
                    .Where(o => o.Key != null && !taken.Contains(o.Key) &&
                                string.Equals(CompanionDeviceTraits.FindDevice(_companions, o.Key)?.Slug, part.DeviceSlug,
                                    StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (sameModel.Count == 1)
                    preselected = sameModel[0];
            }

            if (preselected?.Key != null)
                taken.Add(preselected.Key);

            CompanionPartRow row = new(part.DeviceKey, part.DeviceName ?? part.DeviceKey, options, preselected ?? skip);
            row.PropertyChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(CompanionReplaceWarning));
                OnPropertyChanged(nameof(HasCompanionReplaceWarning));
            };
            CompanionParts.Add(row);
        }
    }

    /// <summary>
    /// Names are not keys anywhere, so a duplicate is only cosmetic — but defaulting to an
    /// obviously distinct name keeps two identically named profiles from confusing the user.
    /// </summary>
    private string DisambiguateName(string name, PackageKind kind)
    {
        if (string.IsNullOrWhiteSpace(name))
            return KindText;

        bool taken = kind switch
        {
            PackageKind.Profile => _config.Profiles?.Any(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) == true,
            PackageKind.Workspace => _config.Profiles?.SelectMany(p => p.Workspaces ?? []).Any(w =>
                string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase)) == true,
            _ => false
        };

        return taken ? $"{name} (imported)" : name;
    }

    private static string BuildProvenance(ProfilePackageManifest manifest)
    {
        List<string> parts = [];

        if (!string.IsNullOrWhiteSpace(manifest.SourceDeviceName))
            parts.Add(manifest.SourceDeviceName);

        if (!string.IsNullOrWhiteSpace(manifest.AppVersion))
            parts.Add($"LoupixDeck {manifest.AppVersion}");

        if (manifest.ExportedUtc != default)
            parts.Add(manifest.ExportedUtc.ToLocalTime().ToString("g"));

        return string.Join("  ·  ", parts);
    }

    private void BuildTargets(PackageKind kind)
    {
        switch (kind)
        {
            case PackageKind.Profile:
                foreach (Profile profile in _config.Profiles ?? [])
                    ReplaceTargets.Add(new ImportTargetRow(profile.Name, profileId: profile.Id));
                break;

            case PackageKind.Workspace:
                ImportTargetLabel = "Import into profile";

                foreach (Profile profile in _config.Profiles ?? [])
                {
                    ImportTargets.Add(new ImportTargetRow(profile.Name, profileId: profile.Id));

                    foreach (Workspace workspace in profile.Workspaces ?? [])
                    {
                        ReplaceTargets.Add(new ImportTargetRow($"{profile.Name} › {workspace.Name}",
                            profileId: profile.Id, workspaceId: workspace.Id));
                    }
                }

                SelectedImportTarget = ImportTargets.FirstOrDefault(t => t.ProfileId == _config.ActiveProfileId)
                                       ?? ImportTargets.FirstOrDefault();
                break;

            case PackageKind.TouchPage:
            case PackageKind.RotaryPage:
                ImportTargetLabel = "Import into workspace";

                foreach (Profile profile in _config.Profiles ?? [])
                {
                    foreach (Workspace workspace in profile.Workspaces ?? [])
                    {
                        ImportTargets.Add(new ImportTargetRow($"{profile.Name} › {workspace.Name}",
                            profileId: profile.Id, workspaceId: workspace.Id));
                    }
                }

                SelectedImportTarget = ImportTargets.FirstOrDefault(t => t.WorkspaceId == _config.ActiveWorkspaceId)
                                       ?? ImportTargets.FirstOrDefault();

                RebuildPageReplaceTargets(kind);
                break;
        }

        SelectedReplaceTarget = ReplaceTargets.FirstOrDefault();
    }

    partial void OnSelectedImportTargetChanged(ImportTargetRow value)
    {
        if (_analysis?.Manifest.Kind is PackageKind.TouchPage or PackageKind.RotaryPage)
            RebuildPageReplaceTargets(_analysis.Manifest.Kind);
    }

    /// <summary>The replaceable pages depend on which workspace was picked, so they are rebuilt
    /// whenever that selection changes.</summary>
    private void RebuildPageReplaceTargets(PackageKind kind)
    {
        ReplaceTargets.Clear();

        Workspace workspace = _config.Profiles?
            .SelectMany(p => p.Workspaces ?? [])
            .FirstOrDefault(w => w.Id == SelectedImportTarget?.WorkspaceId);

        if (workspace == null)
        {
            SelectedReplaceTarget = null;
            return;
        }

        if (kind == PackageKind.TouchPage)
        {
            for (int i = 0; i < workspace.TouchButtonPages.Count; i++)
                ReplaceTargets.Add(new ImportTargetRow(workspace.TouchButtonPages[i].PageName, pageIndex: i));
        }
        else
        {
            RotarySide side = _analysis?.Payload?.RotaryPage?.Side ?? RotarySide.Both;
            IList<RotaryButtonPage> pages = side switch
            {
                RotarySide.Left => workspace.LeftRotaryButtonPages,
                RotarySide.Right => workspace.RightRotaryButtonPages,
                _ => workspace.RotaryButtonPages
            };

            for (int i = 0; i < pages.Count; i++)
                ReplaceTargets.Add(new ImportTargetRow(pages[i].PageName, pageIndex: i));
        }

        SelectedReplaceTarget = ReplaceTargets.FirstOrDefault();
    }

    private async Task ImportAsync()
    {
        if (_analysis == null) return;

        IsImporting = true;

        try
        {
            Result = await _packageService.ImportAsync(_analysis, BuildOptions());
        }
        finally
        {
            // ImportAsync consumes the analysis (its staging folder is gone either way).
            _analysis = null;
            IsImporting = false;
        }

        Confirm(new DialogResult(true));
        CloseWindow?.Invoke();
    }

    private ProfilePackageImportOptions BuildOptions()
    {
        Dictionary<string, MacroConflictResolution> resolutions = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> renames = new(StringComparer.OrdinalIgnoreCase);

        foreach (MacroConflictRow row in Macros)
        {
            resolutions[row.Name] = row.Resolution;
            if (row.Resolution == MacroConflictResolution.Rename)
                renames[row.Name] = row.NewName;
        }

        return new ProfilePackageImportOptions
        {
            Mode = IsReplace ? PackageImportMode.Replace : PackageImportMode.AddAsCopy,
            NewName = NewName,
            ReplaceTargetProfileId = IsReplace ? SelectedReplaceTarget?.ProfileId : null,
            ReplaceTargetWorkspaceId = IsReplace ? SelectedReplaceTarget?.WorkspaceId : null,
            ReplaceTargetPageIndex = IsReplace ? SelectedReplaceTarget?.PageIndex : null,
            TargetProfileId = SelectedImportTarget?.ProfileId,
            TargetWorkspaceId = SelectedImportTarget?.WorkspaceId,
            MacroResolutions = resolutions,
            MacroRenames = renames,
            PluginIdsToEnable = Plugins.Where(p => p.IsDisabled && p.EnableOnImport).Select(p => p.Id).ToList(),
            BackupReplacedItem = BackupReplacedItem,
            CompanionTargets = CompanionParts
                .Where(row => row.SelectedTarget?.Key != null)
                .GroupBy(row => row.PackageKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().SelectedTarget.Key, StringComparer.OrdinalIgnoreCase)
        };
    }
}
