namespace LoupixDeck.Services.Portable;

/// <summary>How an imported item relates to what is already there.</summary>
public enum PackageImportMode
{
    /// <summary>Add as a new item, leaving existing content untouched. Fresh ids.</summary>
    AddAsCopy,

    /// <summary>Replace a selected existing item, keeping its identity so references survive.</summary>
    Replace
}

/// <summary>What to do with an incoming macro whose name is already taken by a different macro.</summary>
public enum MacroConflictResolution
{
    /// <summary>Keep the local macro and do not import the incoming one.</summary>
    Skip,

    /// <summary>Import under a new name and rewrite the package's references to it.</summary>
    Rename,

    /// <summary>Overwrite the local macro with the incoming one.</summary>
    Replace
}

/// <summary>Everything the user decided in the import preview.</summary>
public sealed class ProfilePackageImportOptions
{
    public PackageImportMode Mode { get; set; } = PackageImportMode.AddAsCopy;

    /// <summary>Name for the imported item; defaults to the manifest name.</summary>
    public string NewName { get; set; }

    /// <summary>Profile being replaced (Replace + a profile package).</summary>
    public Guid? ReplaceTargetProfileId { get; set; }

    /// <summary>Workspace being replaced (Replace + a workspace package).</summary>
    public Guid? ReplaceTargetWorkspaceId { get; set; }

    /// <summary>Index of the page being replaced (Replace + a page package).</summary>
    public int? ReplaceTargetPageIndex { get; set; }

    /// <summary>Profile that receives an imported workspace. Defaults to the active profile.</summary>
    public Guid? TargetProfileId { get; set; }

    /// <summary>Workspace that receives an imported page. Defaults to the active workspace.</summary>
    public Guid? TargetWorkspaceId { get; set; }

    /// <summary>Per-macro decisions, keyed by the incoming macro name (case-insensitive).</summary>
    public IReadOnlyDictionary<string, MacroConflictResolution> MacroResolutions { get; set; } =
        new Dictionary<string, MacroConflictResolution>(StringComparer.OrdinalIgnoreCase);

    /// <summary>New names for macros resolved with <see cref="MacroConflictResolution.Rename"/>.</summary>
    public IReadOnlyDictionary<string, string> MacroRenames { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Add the required plugin ids that are installed but not enabled here to this device's
    /// enabled list. Importing never enables a plugin silently.
    /// </summary>
    public bool EnableRequiredPlugins { get; set; }

    /// <summary>Export the item being replaced to a package under the config dir first.</summary>
    public bool BackupReplacedItem { get; set; } = true;
}
