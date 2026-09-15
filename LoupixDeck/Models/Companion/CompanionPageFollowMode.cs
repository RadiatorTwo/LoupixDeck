namespace LoupixDeck.Models.Companion;

/// <summary>
/// Whether a group's companions page along with their master. Pages are matched by position, since a
/// companion's pages are its own. Stored as a number in <c>companions.json</c> so an unknown value
/// from a newer build loads (and acts like <see cref="Off"/>) instead of making the file unreadable.
/// </summary>
public enum CompanionPageFollowMode
{
    /// <summary>Companions page on their own; only profile and workspace follow the master.</summary>
    Off = 0,

    /// <summary>Companions show the master's touch page number.</summary>
    TouchPages = 1,

    /// <summary>Companions show the master's touch and rotary page numbers.</summary>
    TouchAndRotaryPages = 2
}
