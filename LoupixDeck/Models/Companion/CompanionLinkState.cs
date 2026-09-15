namespace LoupixDeck.Models.Companion;

/// <summary>
/// Stored in a companion's own config while it follows a master. The companion's
/// <see cref="LoupedeckConfig.Profiles"/> then hold mirrors of the master's profiles and workspaces
/// (same ids and names, the companion's own pages), and its own profiles wait here unchanged until
/// the device leaves its group.
/// </summary>
public sealed class CompanionLinkState
{
    /// <summary>Scope key of the master the mirrors were built from.</summary>
    public string MasterDeviceKey { get; set; } = string.Empty;

    /// <summary>The companion's own profiles, exactly as they were when it joined.</summary>
    public List<Profile> OwnProfiles { get; set; } = [];

    /// <summary>The companion's own active profile when it joined.</summary>
    public Guid OwnActiveProfileId { get; set; }

    /// <summary>The companion's own startup profile when it joined.</summary>
    public Guid OwnStartupProfileId { get; set; }
}
