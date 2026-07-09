using CommunityToolkit.Mvvm.ComponentModel;

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

    // ── Selection ───────────────────────────────────────────────────────────
    /// <summary>When several rules match the same window, the highest priority wins; ties are
    /// broken by list order (earlier wins), preserving the old first-match-wins behaviour.</summary>
    [ObservableProperty]
    public partial int Priority { get; set; }

    /// <summary>Also apply this rule when its process starts, not only when it gains focus.</summary>
    [ObservableProperty]
    public partial bool ActivateOnProcessStart { get; set; }
}
