using LoupixDeck.Models.Portable;

namespace LoupixDeck.Services.Portable;

/// <summary>How a plugin a package needs relates to the importing machine.</summary>
public enum PackagePluginState
{
    /// <summary>Installed and enabled for this device — nothing to do.</summary>
    Installed,

    /// <summary>Installed, but not in this device's enabled list, so its commands do not resolve here.</summary>
    InstalledButDisabled,

    /// <summary>Not installed at all. Only the manifest knows its name and download link.</summary>
    Missing
}

/// <summary>A plugin requirement of a package, resolved against the importing machine.</summary>
public sealed class PackagePluginStatus
{
    public ProfilePackagePluginRequirement Requirement { get; init; }
    public PackagePluginState State { get; init; }

    /// <summary>Version installed here, when the plugin is present (may differ from the package's).</summary>
    public string InstalledVersion { get; init; }
}

/// <summary>How an incoming macro relates to the local macro set.</summary>
public enum PackageMacroState
{
    /// <summary>No macro of that name exists here — it can be added without asking.</summary>
    New,

    /// <summary>A macro of that name exists and is identical — reuse it silently.</summary>
    Identical,

    /// <summary>A macro of that name exists with different content — the user must decide.</summary>
    Conflicting
}

/// <summary>An incoming macro, resolved against the local macro set.</summary>
public sealed class PackageMacroStatus
{
    public string Name { get; init; }
    public PackageMacroState State { get; init; }
}

/// <summary>
/// Everything known about a package before anything is imported: the manifest, the (not yet
/// remapped, not yet attached) payload, and the classification of its plugins, commands, macros
/// and source device against this machine.
/// </summary>
/// <remarks>
/// Producing this touches nothing outside <see cref="StageDirectory"/> — not the asset store, not
/// macros.json, not the config. That staging folder is the isolation boundary issue #133 asks for;
/// it stays alive until the import runs or <c>DiscardAnalysis</c> throws it away.
/// </remarks>
public sealed class ProfilePackageAnalysis
{
    /// <summary>The package file this analysis came from.</summary>
    public string PackagePath { get; init; }

    /// <summary>Temp folder the package was extracted into. Deleted on import or discard.</summary>
    public string StageDirectory { get; init; }

    public ProfilePackageManifest Manifest { get; init; }

    /// <summary>The deserialized subtree. Not id-remapped and not attached to any config yet.</summary>
    public ProfilePackagePayload Payload { get; init; }

    /// <summary>False only for hard blockers (unreadable package, newer format/schema version).</summary>
    public bool IsImportable { get; init; }

    /// <summary>Why the package cannot be imported; null when <see cref="IsImportable"/>.</summary>
    public string BlockReason { get; init; }

    // ───────── Device capability differences (a warning, never a blocker) ─────────

    public bool DeviceMismatch { get; init; }

    /// <summary>Names both devices and the concrete consequence, e.g. surplus keys.</summary>
    public string DeviceMismatchMessage { get; init; }

    /// <summary>How many source touch keys have no counterpart on this device (0 when none).</summary>
    public int SurplusTouchButtons { get; init; }

    /// <summary>True when the package carries per-side rotary pages this device cannot page.</summary>
    public bool SideStripDataOnNonStripDevice { get; init; }

    // ───────── Contents ─────────

    public IReadOnlyList<PackagePluginStatus> Plugins { get; init; } = [];

    /// <summary>Referenced command names that resolve to nothing on this machine.</summary>
    public IReadOnlyList<string> UnknownCommands { get; init; } = [];

    public IReadOnlyList<PackageMacroStatus> Macros { get; init; } = [];

    /// <summary>Non-fatal notes to show next to the import button.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Convenience for the dialog: only conflicting macros need a user decision.</summary>
    public IEnumerable<PackageMacroStatus> ConflictingMacros =>
        Macros.Where(m => m.State == PackageMacroState.Conflicting);

    public static ProfilePackageAnalysis Blocked(string packagePath, string reason,
        ProfilePackageManifest manifest = null) =>
        new() { PackagePath = packagePath, IsImportable = false, BlockReason = reason, Manifest = manifest };
}
