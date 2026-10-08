using System.Collections.Immutable;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Commands;
using LoupixDeck.Localization;
using LoupixDeck.Services.Actions;
using LoupixDeck.Services.Macros;
using LoupixDeck.Services.Portable;
using LoupixDeck.Utils;

namespace LoupixDeck.ViewModels;

/// <summary>One required plugin, as shown in the import preview.</summary>
public sealed partial class PackagePluginRow : ObservableObject
{
    private readonly PackagePluginStatus _status;

    public PackagePluginRow(PackagePluginStatus status)
    {
        _status = status;
        EnableOnImport = status.State == PackagePluginState.InstalledButDisabled;
    }

    public string Id => _status.Requirement.Id;

    public string Name => string.IsNullOrWhiteSpace(_status.Requirement.Name)
        ? _status.Requirement.Id
        : _status.Requirement.Name;

    public string VersionText => _status.State == PackagePluginState.Missing
        ? $"required: {_status.Requirement.Version ?? "any"}"
        : $"installed: {_status.InstalledVersion ?? "unknown"} (package: {_status.Requirement.Version ?? "any"})";

    public string StateText => _status.State switch
    {
        PackagePluginState.Installed => "Installed",
        PackagePluginState.InstalledButDisabled => "Disabled for this device",
        _ => "Not installed"
    };

    public bool IsInstalled => _status.State == PackagePluginState.Installed;
    public bool IsDisabled => _status.State == PackagePluginState.InstalledButDisabled;
    public bool IsMissing => _status.State == PackagePluginState.Missing;

    public bool HasProjectUrl => OpenUrlCommand.TryNormalize(_status.Requirement.ProjectUrl, out _);

    /// <summary>Only meaningful for a disabled plugin; the import adds it to the enabled list.</summary>
    [ObservableProperty]
    public partial bool EnableOnImport { get; set; }

    public IRelayCommand OpenProjectCommand => field ??= Relay.Create(() =>
    {
        // The address comes from the imported package, so it must never reach the shell as a
        // path or program: only http(s) is opened.
        if (HasProjectUrl)
            OpenUrlCommand.TryOpen(_status.Requirement.ProjectUrl);
    });
}

/// <summary>One incoming macro whose name is already taken by a different local macro.</summary>
public sealed partial class MacroConflictRow : ObservableObject
{
    public MacroConflictRow(string name)
    {
        Name = name;
        NewName = $"{name} (imported)";
    }

    public string Name { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRename))]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    public partial MacroConflictResolution Resolution { get; set; } = MacroConflictResolution.Skip;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    public partial string NewName { get; set; }

    public bool IsRename => Resolution == MacroConflictResolution.Rename;

    /// <summary>
    /// A rename must produce a usable macro name — the command parser would choke on
    /// <c>( ) , &amp;</c>, which is why <c>MacroManager</c> forbids them.
    /// </summary>
    public bool IsValid => !IsRename || MacroManager.HasValidNameCharacters(NewName);

    /// <summary>All resolutions — bound by the row's ComboBox.</summary>
    public static ImmutableArray<MacroConflictResolution> AllResolutions { get; } =
        ImmutableCollectionsMarshal.AsImmutableArray(Enum.GetValues<MacroConflictResolution>());
}

/// <summary>
/// A container the import can go into, or an existing item it can replace. Exactly one of the id
/// fields is set, matching what the package's kind needs.
/// </summary>
public sealed class ImportTargetRow(string label, Guid? profileId = null, Guid? workspaceId = null,
    int? pageIndex = null)
{
    public string Label { get; } = label;
    public Guid? ProfileId { get; } = profileId;
    public Guid? WorkspaceId { get; } = workspaceId;
    public int? PageIndex { get; } = pageIndex;
}

/// <summary>
/// One entry of the layout template list of an import dialog; a null template leaves the buttons as
/// they are.
/// </summary>
public sealed record LayoutTemplateOption(ButtonTemplate? Template, string Label)
{
    /// <summary>"None" followed by every template, labelled as the touch button editor labels them.</summary>
    public static IReadOnlyList<LayoutTemplateOption> CreateAll() =>
    [
        new(null, Loc.Tr("ProfileImport_LayoutTemplateNone")),
        new(ButtonTemplate.IconCaptionBottom, Loc.Tr("TouchButton_Template_IconTextBottom")),
        new(ButtonTemplate.IconCaptionTop, Loc.Tr("TouchButton_Template_IconTextTop")),
        new(ButtonTemplate.IconOnly, Loc.Tr("TouchButton_Template_IconOnly")),
        new(ButtonTemplate.TextOnly, Loc.Tr("TouchButton_Template_TextOnly"))
    ];

    /// <summary>What a template did to an imported item, as one line for the result notice.</summary>
    public static string DescribeResult(TemplateApplyResult result) =>
        Loc.Tr("ProfileImport_LayoutTemplateResult", result.ButtonsChanged, result.LayersRemoved);

    /// <summary>The lines a package result is reported with: its message, what a template did, the warnings.</summary>
    public static IEnumerable<string> ResultLines(ProfilePackageResult result)
    {
        yield return result.Message;

        if (result.LayoutTemplate is { } laidOut)
            yield return DescribeResult(laidOut);

        foreach (string warning in result.Warnings)
            yield return warning;
    }
}

/// <summary>A companion of this master a companion part can be imported onto; a null key skips the part.</summary>
public sealed record CompanionTargetOption(string Key, string Label);

/// <summary>One companion's pages in the package, and the companion of this master that receives them.</summary>
public sealed partial class CompanionPartRow : ObservableObject
{
    public CompanionPartRow(string packageKey, string deviceName, IReadOnlyList<CompanionTargetOption> options,
        CompanionTargetOption selected)
    {
        PackageKey = packageKey;
        DeviceName = deviceName;
        Options = options;
        SelectedTarget = selected;
    }

    /// <summary>The companion's key on the exporting machine.</summary>
    public string PackageKey { get; }

    public string DeviceName { get; }

    public IReadOnlyList<CompanionTargetOption> Options { get; }

    [ObservableProperty]
    public partial CompanionTargetOption SelectedTarget { get; set; }
}
