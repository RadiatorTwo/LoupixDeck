namespace LoupixDeck.Models;

/// <summary>
/// What the context engine does with the active profile when the foreground app matches no
/// <see cref="ContextRule"/> any more.
/// </summary>
public enum NoMatchProfileBehavior
{
    /// <summary>Leave the profile alone: whatever the last matching rule activated stays active.</summary>
    KeepCurrent,

    /// <summary>Go back to the profile that was active before a rule first took over.</summary>
    RestorePrevious,

    /// <summary>Switch to the profile named by <see cref="LoupedeckConfig.FallbackProfileId"/>.</summary>
    FixedProfile
}
