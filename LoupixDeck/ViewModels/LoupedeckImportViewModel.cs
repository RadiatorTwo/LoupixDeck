using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Controllers;
using LoupixDeck.Localization;
using LoupixDeck.Models;
using LoupixDeck.Models.Macros;
using LoupixDeck.Registry;
using LoupixDeck.Services;
using LoupixDeck.Services.Import.Lp5;
using LoupixDeck.Services.Macros;
using LoupixDeck.Services.Portable;
using LoupixDeck.Services.Profiles;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels;

/// <summary>One Loupedeck action the import cannot carry over, as shown in the preview.</summary>
public sealed record LoupedeckUnmappedRow(string Label, string Location, string Reason);

/// <summary>
/// Import preview for a Loupedeck <c>.lp5</c> profile export: shows how many controls translate, which
/// actions stay behind and why, and which macros are created, then adds the profile on confirmation.
/// </summary>
/// <remarks>
/// Reading and planning never touch the config, the macro store or the asset store; everything is
/// written by <see cref="ImportAsync"/> only.
/// </remarks>
public sealed partial class LoupedeckImportViewModel : DialogViewModelBase<DialogResult>, IAsyncInitViewModel
{
    private readonly LoupedeckConfig _config;
    private readonly IMacroManager _macroManager;
    private readonly IAssetService _assets;
    private readonly IDeviceService _deviceService;
    private readonly IPageManager _pageManager;
    private readonly IDeviceController _controller;
    private readonly DeviceGeometry _geometry;

    private string _path;
    private Lp5ImportPlan _plan;

    public LoupedeckImportViewModel(LoupedeckConfig config, IMacroManager macroManager, IAssetService assets,
        IDeviceService deviceService, IPageManager pageManager, IDeviceController controller, DeviceGeometry geometry)
    {
        _config = config;
        _macroManager = macroManager;
        _assets = assets;
        _deviceService = deviceService;
        _pageManager = pageManager;
        _controller = controller;
        _geometry = geometry ?? DeviceGeometry.Default;

        Unmapped = new();
        MacroNames = new();
        Notes = new();
    }

    /// <summary>
    /// Asks for a <c>.lp5</c> file, shows the preview and returns the result message, or null when the
    /// user cancelled either step.
    /// </summary>
    public static async Task<string> ShowAsync(IDialogService dialogService)
    {
        string source = await FileDialogHelper.OpenLoupedeckProfileDialog(WindowHelper.GetActiveWindow());
        if (string.IsNullOrEmpty(source))
            return null;

        LoupedeckImportViewModel viewModel = null;
        DialogResult dialogResult = await dialogService.ShowDialogAsync<LoupedeckImportViewModel, DialogResult>(vm =>
        {
            viewModel = vm;
            vm.Configure(source);
        });

        return dialogResult?.IsConfirmed == true ? viewModel?.ResultMessage : null;
    }

    public void Configure(string path) => _path = path;

    public event Action CloseWindow;

    /// <summary>Summary of the finished import; null until it ran.</summary>
    public string ResultMessage { get; private set; }

    // ───────── State ─────────

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
    public partial bool IsImporting { get; set; }

    [ObservableProperty]
    public partial string NewName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SourceText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SummaryText { get; set; } = string.Empty;

    public ObservableCollection<LoupedeckUnmappedRow> Unmapped { get; }
    public ObservableCollection<string> MacroNames { get; }
    public ObservableCollection<string> Notes { get; }

    public bool HasUnmapped => Unmapped.Count > 0;
    public bool HasMacros => MacroNames.Count > 0;
    public bool HasNotes => Notes.Count > 0;

    // ───────── App link ─────────

    /// <summary>True when the file names an application a profile rule can target.</summary>
    [ObservableProperty]
    public partial bool CanLinkApp { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEnableSwitching))]
    public partial bool LinkApp { get; set; }

    [ObservableProperty]
    public partial string LinkAppText { get; set; } = string.Empty;

    /// <summary>Set when the application already switches to another profile; linking moves that rule.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLinkConflict))]
    public partial string LinkConflictText { get; set; }

    public bool HasLinkConflict => !string.IsNullOrEmpty(LinkConflictText);

    [ObservableProperty]
    public partial bool EnableSwitching { get; set; } = true;

    /// <summary>Offered only when a link is made while automatic switching is off.</summary>
    public bool ShowEnableSwitching => LinkApp && !_config.AppSwitchingEnabled;

    // ───────── Commands ─────────

    public IAsyncRelayCommand ImportCommand => field ??= Relay.Create(ImportAsync, () => CanImport);

    public bool CanImport => _plan != null && !IsImporting && !string.IsNullOrWhiteSpace(NewName);

    public IRelayCommand CancelCommand => field ??= Relay.Create(() =>
    {
        Cancel();
        CloseWindow?.Invoke();
    });

    partial void OnNewNameChanged(string value) => ImportCommand.NotifyCanExecuteChanged();

    partial void OnIsImportingChanged(bool value) => ImportCommand.NotifyCanExecuteChanged();

    public async Task InitializeAsync()
    {
        HashSet<string> macroNames = new(_macroManager.Macros.Select(m => m.Name), StringComparer.OrdinalIgnoreCase);
        Lp5DeviceShape shape = new(_deviceService.TouchButtonCount, _deviceService.RotaryButtonCount,
            _pageManager.SideRotaryButtonCount, _pageManager.HasIndependentRotarySides);

        try
        {
            _plan = await Task.Run(() => Lp5ImportPlan.Create(Lp5Package.Load(_path), shape, macroNames.Contains));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            BlockReason = Loc.Tr("LoupedeckImport_CannotRead", ex.Message);
            IsLoading = false;
            return;
        }

        Lp5Package package = _plan.Package;
        NewName = DisambiguateName(package.Name);
        SourceText = string.Join("  ·  ", new[] { package.DeviceType, package.ApplicationName }.Where(s => !string.IsNullOrWhiteSpace(s)));
        SummaryText = Loc.Tr("LoupedeckImport_Summary", _plan.MappedControls, _plan.TotalControls,
            _plan.Workspaces.Sum(w => w.TouchPages.Count), _plan.Workspaces.Sum(w => w.RotaryPages.Count));

        foreach (Lp5UnmappedControl control in _plan.Unmapped)
            Unmapped.Add(new LoupedeckUnmappedRow(string.IsNullOrWhiteSpace(control.Label) ? "—" : control.Label,
                control.Location, DescribeReason(control.Reason, control.Detail)));

        foreach (Lp5MacroDraft macro in _plan.Macros)
            MacroNames.Add(macro.Name);

        foreach (Lp5Note note in _plan.Notes)
            Notes.Add(DescribeNote(note));

        BuildAppLink(package);

        IsLoading = false;
        OnPropertyChanged(nameof(HasUnmapped));
        OnPropertyChanged(nameof(HasMacros));
        OnPropertyChanged(nameof(HasNotes));
        ImportCommand.NotifyCanExecuteChanged();
    }

    private void BuildAppLink(Lp5Package package)
    {
        string process = package.ProcessName;
        CanLinkApp = (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) && ProfileAppLink.CanLinkProcess(process);
        if (!CanLinkApp)
            return;

        string appName = string.IsNullOrWhiteSpace(package.ApplicationName) ? process : package.ApplicationName;
        LinkAppText = Loc.Tr("LoupedeckImport_LinkApp", appName);

        // Guid.Empty stands for the profile that does not exist yet: every existing rule is "another profile".
        IReadOnlyList<ContextRule> conflicts = ProfileAppLink.FindConflictingRules(_config.ContextRules, Guid.Empty, process);
        if (conflicts.Count > 0)
            LinkConflictText = Loc.Tr("LoupedeckImport_LinkConflict", appName);

        // Taking over an existing link is a deliberate choice, so it starts unticked.
        LinkApp = conflicts.Count == 0;
    }

    private Task ImportAsync()
    {
        if (_plan == null) return Task.CompletedTask;

        IsImporting = true;
        try
        {
            string name = NewName.Trim();

            // Macros first: the profile's buttons refer to them by name.
            List<Macro> macros = Lp5ProfileBuilder.BuildMacros(_plan);
            if (macros.Count > 0)
                _macroManager.ReplaceAll([.. _macroManager.Macros, .. macros]);

            Profile profile = Lp5ProfileBuilder.BuildProfile(_plan, name, _assets, _geometry.KeySize);
            PortablePayloadNormalizer.Normalize(profile, _deviceService.TouchButtonCount,
                _deviceService.RotaryButtonCount, _pageManager.SideRotaryButtonCount);

            _config.Profiles.Add(profile);

            if (CanLinkApp && LinkApp)
            {
                foreach (ContextRule conflict in ProfileAppLink.FindConflictingRules(_config.ContextRules, profile.Id, _plan.Package.ProcessName))
                    _config.ContextRules.Remove(conflict);

                ProfileAppLink.Link(_config.ContextRules, profile.Id, _plan.Package.ProcessName);
                if (ShowEnableSwitching && EnableSwitching)
                    _config.AppSwitchingEnabled = true;
            }

            // Saved in the same continuation as the asset import: another device's save sweeps assets
            // that no config on disk references yet.
            _controller.SaveConfig();

            ResultMessage = Loc.Tr("LoupedeckImport_Done", profile.Name, _plan.MappedControls, _plan.TotalControls);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[lp5] Import failed: {ex}");
            ResultMessage = Loc.Tr("LoupedeckImport_Failed", ex.Message);
        }
        finally
        {
            IsImporting = false;
        }

        Confirm(new DialogResult(true));
        CloseWindow?.Invoke();
        return Task.CompletedTask;
    }

    private string DisambiguateName(string name)
    {
        bool taken = _config.Profiles?.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) == true;
        return taken ? $"{name} (Loupedeck)" : name;
    }

    private static string DescribeReason(Lp5UnmappedReason reason, string detail) => reason switch
    {
        Lp5UnmappedReason.PluginAction => Loc.Tr("LoupedeckImport_ReasonPlugin", detail ?? string.Empty),
        Lp5UnmappedReason.UnknownKey => Loc.Tr("LoupedeckImport_ReasonUnknownKey", detail ?? string.Empty),
        Lp5UnmappedReason.MissingDefinition => Loc.Tr("LoupedeckImport_ReasonMissing"),
        Lp5UnmappedReason.WrongControl => Loc.Tr("LoupedeckImport_ReasonWrongControl"),
        _ => string.IsNullOrEmpty(detail)
            ? Loc.Tr("LoupedeckImport_ReasonUnsupportedGeneric")
            : Loc.Tr("LoupedeckImport_ReasonUnsupported", detail)
    };

    private static string DescribeNote(Lp5Note note) => note.Kind switch
    {
        Lp5NoteKind.PageSplit => Loc.Tr("LoupedeckImport_NotePageSplit", note.Detail ?? string.Empty),
        Lp5NoteKind.FnActionsSkipped => Loc.Tr("LoupedeckImport_NoteFnActions", note.Count),
        Lp5NoteKind.NoDials => Loc.Tr("LoupedeckImport_NoteNoDials", note.Count),
        _ => Loc.Tr("LoupedeckImport_NoteEmpty")
    };
}
