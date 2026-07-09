using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;

namespace LoupixDeck.Models;

/// <summary>
/// A profile represents an application or usage scenario (e.g. "Desktop", "OBS", "Rider")
/// and groups one or more <see cref="Workspace"/>s (issue #132). Exactly one profile is
/// active on a device at a time; activating it opens its <see cref="HomeWorkspaceId">home
/// workspace</see> by default. Context rules that activate profiles automatically are added
/// in a later phase (#132) as additive, optional fields.
/// </summary>
public partial class Profile : ObservableObject
{
    public Profile()
    {
        // Assign via the generated setter so any future setter-driven wiring runs
        // (mirrors the Workspace/LoupedeckConfig collection-init gotcha).
        Workspaces = new();
    }

    /// <summary>Stable identity used by commands and context rules to target this profile.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>User-assigned profile name (e.g. "OBS", "Rider").</summary>
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ObservableCollection<Workspace> Workspaces { get; set; }

    /// <summary>
    /// Id of the workspace opened when this profile is activated (the return target inside the
    /// profile). Falls back to the first workspace when it does not resolve.
    /// </summary>
    public Guid HomeWorkspaceId { get; set; }

    /// <summary>
    /// Relative priority used to break ties when several profiles' context rules match the same
    /// foreground window. Higher wins. Default 0. (Consumed by the rule engine in a later phase.)
    /// </summary>
    public int Priority { get; set; }

    /// <summary>The home workspace resolved from <see cref="HomeWorkspaceId"/>, or the first
    /// workspace when the id does not resolve. Null only when the profile has no workspaces.</summary>
    [JsonIgnore]
    public Workspace HomeWorkspace =>
        Workspaces?.FirstOrDefault(w => w.Id == HomeWorkspaceId)
        ?? Workspaces?.FirstOrDefault();
}
