namespace LoupixDeck.Models.Companion;

/// <summary>
/// A device's role inside the companion system, derived from the group it belongs to
/// (see <see cref="CompanionGroup"/>). Never persisted per device — the group is the single
/// source of truth, so a device can never be recorded as both master and companion.
/// </summary>
public enum CompanionRole
{
    /// <summary>The device belongs to no companion group and behaves as a standalone unit.</summary>
    None,

    /// <summary>The device controls the companions of its group.</summary>
    Master,

    /// <summary>The device is controlled by its group's master. It keeps its own configuration and
    /// its local actions, but never drives another device.</summary>
    Companion
}
