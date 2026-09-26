using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Controllers;
using LoupixDeck.Localization;
using LoupixDeck.Models;
using LoupixDeck.Registry;
using LoupixDeck.Services;
using LoupixDeck.Services.Import.Lp5;
using LoupixDeck.Services.Portable;
using LoupixDeck.Services.Profiles;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels;

/// <summary>One Loupedeck action the import cannot carry over, as shown in the preview.</summary>
public sealed record LoupedeckUnmappedRow(string Label, string Location, string Reason);

/// <summary>
/// Import preview for a Loupedeck <c>.lp5</c> profile export: shows how many controls translate and which
/// actions stay behind and why, then adds the profile on confirmation.
/// </summary>
/// <remarks>
/// The preview converts without an asset store and never touches the config; everything is written by
/// <see cref="ImportAsync"/> only.
/// </remarks>
public sealed partial class LoupedeckImportViewModel : DialogViewModelBase<DialogResult>, IAsyncInitViewModel
{
    private readonly LoupedeckConfig _config;
    private readonly IAssetService _assets;
    private readonly IDeviceService _deviceService;
    private readonly IPageManager _pageManager;
    private readonly IDeviceController _controller;
    private readonly DeviceGeometry _geometry;

    private string _path;
    private Lp5Archive _archive;
    private Lp5ConversionResult _preview;

    public LoupedeckImportViewModel(LoupedeckConfig config, IAssetService assets, IDeviceService deviceService,
        IPageManager pageManager, IDeviceController controller, DeviceGeometry geometry)
    {
        _config = config;
        _assets = assets;
        _deviceService = deviceService;
        _pageManager = pageManager;
        _controller = controller;
        _geometry = geometry ?? DeviceGeometry.Default;

        Unmapped = new();
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
    public ObservableCollection<string> Notes { get; }

    public bool HasUnmapped => Unmapped.Count > 0;
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

    public bool CanImport => _preview != null && !IsImporting && !string.IsNullOrWhiteSpace(NewName);

    public IRelayCommand CancelCommand => field ??= Relay.Create(() =>
    {
        Cancel();
        CloseWindow?.Invoke();
    });

    partial void OnNewNameChanged(string value) => ImportCommand.NotifyCanExecuteChanged();

    partial void OnIsImportingChanged(bool value) => ImportCommand.NotifyCanExecuteChanged();

    private Lp5DeviceShape Shape => new(_deviceService.TouchButtonCount, _deviceService.RotaryButtonCount,
        _pageManager.SideRotaryButtonCount, _pageManager.HasIndependentRotarySides, _geometry);

    public async Task InitializeAsync()
    {
        Lp5DeviceShape shape = Shape;

        try
        {
            (_archive, _preview) = await Task.Run(() =>
            {
                Lp5Archive archive = Lp5Archive.Open(_path);
                return (archive, Lp5Converter.Convert(archive, shape, assets: null));
            });
        }
        catch (Exception ex)
        {
            // The dialog runs this fire-and-forget: anything left uncaught would leave it loading forever.
            // Unreadable ZIP entries, for instance, throw NotSupportedException.
            Console.WriteLine($"[lp5] Cannot read '{_path}': {ex}");
            BlockReason = Loc.Tr("LoupedeckImport_CannotRead", ex.Message);
            IsLoading = false;
            return;
        }

        NewName = DisambiguateName(_archive.ProfileName);
        SourceText = string.Join("  ·  ",
            new[] { _archive.DeviceType, _archive.ApplicationName }.Where(s => !string.IsNullOrWhiteSpace(s)));
        SummaryText = Loc.Tr("LoupedeckImport_Summary", _preview.MappedControls, _preview.TotalControls,
            _preview.TouchPages, _preview.RotaryPages);

        foreach (Lp5UnsupportedControl control in _preview.Unsupported)
        {
            Unmapped.Add(new LoupedeckUnmappedRow(string.IsNullOrWhiteSpace(control.Label) ? "—" : control.Label,
                control.Location, DescribeReason(control.Reason, control.Detail)));
        }

        foreach (Lp5Note note in _preview.Notes)
            Notes.Add(DescribeNote(note));

        BuildAppLink();

        IsLoading = false;
        OnPropertyChanged(nameof(HasUnmapped));
        OnPropertyChanged(nameof(HasNotes));
        ImportCommand.NotifyCanExecuteChanged();
    }

    private void BuildAppLink()
    {
        string process = _archive.ProcessName;
        CanLinkApp = (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) && ProfileAppLink.CanLinkProcess(process);
        if (!CanLinkApp)
            return;

        string appName = string.IsNullOrWhiteSpace(_archive.ApplicationName) ? process : _archive.ApplicationName;
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
        if (_preview == null) return Task.CompletedTask;

        IsImporting = true;
        try
        {
            // The real pass stores the icons; the preview pass stored nothing.
            Lp5ConversionResult result = Lp5Converter.Convert(_archive, Shape, _assets);
            Profile profile = result.Profile;
            profile.Name = NewName.Trim();
            PortablePayloadNormalizer.Normalize(profile, _deviceService.TouchButtonCount,
                _deviceService.RotaryButtonCount, _pageManager.SideRotaryButtonCount);

            _config.Profiles.Add(profile);

            if (CanLinkApp && LinkApp)
            {
                foreach (ContextRule conflict in ProfileAppLink.FindConflictingRules(_config.ContextRules, profile.Id, _archive.ProcessName))
                    _config.ContextRules.Remove(conflict);

                ProfileAppLink.Link(_config.ContextRules, profile.Id, _archive.ProcessName);
                if (ShowEnableSwitching && EnableSwitching)
                    _config.AppSwitchingEnabled = true;
            }

            // Saved in the same continuation as the asset import: another device's save sweeps assets
            // that no config on disk references yet.
            _controller.SaveConfig();

            ResultMessage = Loc.Tr("LoupedeckImport_Done", profile.Name, result.MappedControls, result.TotalControls);
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

    private static string DescribeReason(Lp5UnsupportedReason reason, string detail) => reason switch
    {
        Lp5UnsupportedReason.UnknownAction => Loc.Tr("LoupedeckImport_ReasonUnknownAction", detail ?? string.Empty),
        Lp5UnsupportedReason.UnsupportedProfileAction => Loc.Tr("LoupedeckImport_ReasonProfileAction", detail ?? string.Empty),
        Lp5UnsupportedReason.UnsupportedMacro => Loc.Tr("LoupedeckImport_ReasonMacro"),
        Lp5UnsupportedReason.MissingDefinition => Loc.Tr("LoupedeckImport_ReasonMissing"),
        Lp5UnsupportedReason.PageOutsideWorkspace => Loc.Tr("LoupedeckImport_ReasonPageOutsideWorkspace"),
        Lp5UnsupportedReason.UnsupportedAdjustment => Loc.Tr("LoupedeckImport_ReasonAdjustment", detail ?? string.Empty),
        _ => Loc.Tr("LoupedeckImport_ReasonRecursion")
    };

    private static string DescribeNote(Lp5Note note) => note.Kind switch
    {
        Lp5NoteKind.WheelPagesSkipped => Loc.Tr("LoupedeckImport_NoteWheelPages", note.Count),
        Lp5NoteKind.SurplusKeys => Loc.Tr("LoupedeckImport_NoteSurplusKeys", note.Count),
        Lp5NoteKind.SurplusDials => Loc.Tr("LoupedeckImport_NoteSurplusDials", note.Count),
        _ => Loc.Tr("LoupedeckImport_NoteUnreadableIcons", note.Count)
    };
}
