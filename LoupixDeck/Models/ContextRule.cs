using CommunityToolkit.Mvvm.ComponentModel;
using LoupixDeck.Models.Companion;
using Newtonsoft.Json;

namespace LoupixDeck.Models;

/// <summary>
/// One context rule (issue #132): when the foreground window matches <see cref="ProcessName"/>
/// (and optionally contains <see cref="TitleContains"/>), the rule activates a profile and/or a
/// workspace, optionally jumping to a specific page inside the resulting workspace. Rules are the
/// profile/workspace-aware successor to <see cref="AppPageBinding"/>; a migrated app-switching
/// binding becomes a rule that activates the default profile's home workspace and applies the old
/// page index, so behaviour is preserved.
/// </summary>
[ObservableObject]
public sealed partial class ContextRule
{
    // ── Match ──────────────────────────────────────────────────────────────
    [ObservableProperty]
    public partial string ProcessName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TitleContains { get; set; } = string.Empty;

    // ── Actions (null = leave that dimension unchanged) ─────────────────────
    /// <summary>Profile to activate; activating a profile opens its home workspace.</summary>
    [ObservableProperty]
    public partial Guid? ActivateProfileId { get; set; }

    /// <summary>Workspace to activate within the resulting profile (overrides the home workspace).</summary>
    [ObservableProperty]
    public partial Guid? ActivateWorkspaceId { get; set; }

    /// <summary>Optional 0-based touch page to jump to inside the resulting workspace (carried over
    /// from a migrated <see cref="AppPageBinding"/>).</summary>
    [ObservableProperty]
    public partial int? TouchPageIndex { get; set; }

    /// <summary>Optional 0-based rotary page to jump to inside the resulting workspace.</summary>
    [ObservableProperty]
    public partial int? RotaryPageIndex { get; set; }

    /// <summary>Touch page to jump to, by stable id (since config v13). Wins over
    /// <see cref="TouchPageIndex"/> when it resolves inside the resulting workspace.</summary>
    [ObservableProperty]
    public partial Guid? TouchPageId { get; set; }

    /// <summary>Rotary page to jump to, by stable id (since config v13). Wins over
    /// <see cref="RotaryPageIndex"/> when it resolves inside the resulting workspace.</summary>
    [ObservableProperty]
    public partial Guid? RotaryPageId { get; set; }

    /// <summary>Left dial-column rotary page to jump to on a side-strip device, by stable id.</summary>
    [ObservableProperty]
    public partial Guid? LeftRotaryPageId { get; set; }

    /// <summary>Right dial-column rotary page to jump to on a side-strip device, by stable id.</summary>
    [ObservableProperty]
    public partial Guid? RightRotaryPageId { get; set; }

    /// <summary>
    /// Pages to open on this device's companions when the rule applies, inside the workspace they
    /// share with it. Only used while this device is a master; kept unchanged otherwise. Null (and
    /// not written) when the rule has none, so rules saved before it load and save unchanged.
    /// </summary>
    [ObservableProperty]
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public partial List<CompanionPageTarget> CompanionPageTargets { get; set; }

    // ── Selection ───────────────────────────────────────────────────────────
    /// <summary>When several rules match the same window, the highest priority wins; ties are
    /// broken by list order (earlier wins), preserving the old first-match-wins behaviour.</summary>
    [ObservableProperty]
    public partial int Priority { get; set; }

    /// <summary>Also apply this rule when its process starts, not only when it gains focus.</summary>
    [ObservableProperty]
    public partial bool ActivateOnProcessStart { get; set; }
}
