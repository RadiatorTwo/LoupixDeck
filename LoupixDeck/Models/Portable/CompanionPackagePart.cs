using Newtonsoft.Json;

namespace LoupixDeck.Models.Portable;

/// <summary>
/// A companion's own content inside a package a master exported: the companion's mirror of the
/// exported profile or workspace, which carries the master's ids but the companion's own pages (and,
/// for a profile, its own LED buttons). On import into a master it is handed to one of that master's
/// companions and put into its mirror.
/// </summary>
/// <remarks>
/// Lives in <see cref="ProfilePackagePayload.Companions"/>, so the export's asset, macro and plugin
/// scans cover it without knowing it exists, and the id remapping on import keeps the master's
/// and the companions' ids consistent in one pass.
/// </remarks>
public sealed class CompanionPackagePart
{
    /// <summary>Scope key of the companion on the exporting machine. Commands of the package that
    /// address the companion (<c>Companion.GotoTouchPage(&lt;key&gt;, …)</c>) carry this key and are
    /// rewritten to the companion chosen on import.</summary>
    public string DeviceKey { get; set; }

    /// <summary>Device slug of the companion, e.g. <c>razer-stream-controller</c>.</summary>
    public string DeviceSlug { get; set; }

    /// <summary>Human-readable name of the companion at export time.</summary>
    public string DeviceName { get; set; }

    /// <summary>Touch-button count of the companion (its pages carry that many buttons).</summary>
    public int TouchButtonCount { get; set; }

    /// <summary>Rotary-button count of the companion.</summary>
    public int RotaryButtonCount { get; set; }

    /// <summary>True when the companion pages its dial columns independently (side strips).</summary>
    public bool HasSideStrips { get; set; }

    /// <summary>The companion's mirror of the exported profile (package kind Profile).</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public Profile Profile { get; set; }

    /// <summary>The companion's mirror of the exported workspace (package kind Workspace).</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public Workspace Workspace { get; set; }
}
