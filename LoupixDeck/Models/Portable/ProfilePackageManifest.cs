using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace LoupixDeck.Models.Portable;

/// <summary>
/// <c>manifest.json</c> of a <c>.loupixprofile</c> package (issue #133). Read on its own, before
/// anything else in the package, so an unsupported package can be rejected without touching the
/// payload.
/// </summary>
/// <remarks>
/// Two independent version axes:
/// <list type="bullet">
/// <item><see cref="FormatVersion"/> describes the package envelope itself (this class, the file
/// layout inside the zip). Newer than the host = refuse; older = run the package migration chain.</item>
/// <item><see cref="ConfigSchemaVersion"/> describes the config schema the payload models were
/// written with (<c>LoupedeckConfig.CurrentVersion</c>). The config migrators operate on a whole
/// config root and cannot be applied to a subtree, so an older value is only a warning (every
/// field added since is additive with a working default) and a newer value is refused.</item>
/// </list>
/// Adding a field to this class is backward compatible in both directions and needs no version
/// bump: Newtonsoft ignores unknown members when reading, and an older host simply drops the
/// new field.
/// </remarks>
public sealed class ProfilePackageManifest
{
    /// <summary>Envelope schema version written by this build.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>Envelope schema version of this package. See the class remarks.</summary>
    public int FormatVersion { get; set; } = CurrentFormatVersion;

    /// <summary>Config schema version the payload was serialized with. See the class remarks.</summary>
    public int ConfigSchemaVersion { get; set; }

    /// <summary>Which of the payload slots is populated.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public PackageKind Kind { get; set; }

    /// <summary>Display name of the exported item (profile/workspace/page name).</summary>
    public string Name { get; set; }

    /// <summary>Optional free-text description supplied by the exporting user.</summary>
    public string Description { get; set; }

    // ───────── Source device (drives the import-time capability warning) ─────────

    /// <summary>Device slug the package was exported from, e.g. <c>loupedeck-live-s</c>.</summary>
    public string SourceDeviceSlug { get; set; }

    /// <summary>Human-readable device name, e.g. "Loupedeck Live S".</summary>
    public string SourceDeviceName { get; set; }

    /// <summary>Touch-button count of the source device (pages carry that many buttons).</summary>
    public int SourceTouchButtonCount { get; set; }

    /// <summary>Rotary-button count of the source device.</summary>
    public int SourceRotaryButtonCount { get; set; }

    /// <summary>True when the source device pages its dial columns independently (side strips).</summary>
    public bool SourceHasSideStrips { get; set; }

    // ───────── Provenance ─────────

    /// <summary>LoupixDeck version that produced the package (informational).</summary>
    public string AppVersion { get; set; }

    /// <summary>When the package was written.</summary>
    public DateTimeOffset ExportedUtc { get; set; }

    // ───────── Contents ─────────

    /// <summary>Plugins the exported subtree binds commands or side strips to.</summary>
    public List<ProfilePackagePluginRequirement> RequiredPlugins { get; set; } = [];

    /// <summary>Distinct command names referenced anywhere in the subtree (including via macros).</summary>
    public List<string> ReferencedCommands { get; set; } = [];

    /// <summary>Asset-relative paths actually included under <c>assets/</c> in the package.</summary>
    public List<string> Assets { get; set; } = [];

    /// <summary>Names of the macros included in the package's <c>macros.json</c>.</summary>
    public List<string> Macros { get; set; } = [];

    /// <summary>Name of the payload file inside the package. Indirection for future formats.</summary>
    public string PayloadFile { get; set; } = ProfilePackageFiles.Payload;
}
