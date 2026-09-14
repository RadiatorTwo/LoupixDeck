namespace LoupixDeck.Models.Companion;

/// <summary>
/// The mirrored profiles of a device that left its group, with the pages it had built inside them.
/// Joining the same master again restores them, so leaving a group by mistake loses nothing.
/// </summary>
public sealed class ParkedCompanionProfiles
{
    /// <summary>Scope key of the master the mirrors belong to.</summary>
    public string MasterDeviceKey { get; set; } = string.Empty;

    public List<Profile> Profiles { get; set; } = [];
}
