using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    public bool HasProjectUrl => !string.IsNullOrWhiteSpace(_status.Requirement.ProjectUrl);

    /// <summary>Only meaningful for a disabled plugin; the import adds it to the enabled list.</summary>
    [ObservableProperty]
    public partial bool EnableOnImport { get; set; }

    public IRelayCommand OpenProjectCommand => field ??= Relay.Create(() =>
    {
        if (!HasProjectUrl) return;

        try
        {
            Process.Start(new ProcessStartInfo(_status.Requirement.ProjectUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ProfileImport] Could not open '{_status.Requirement.ProjectUrl}': {ex.Message}");
        }
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
