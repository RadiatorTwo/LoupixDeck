namespace LoupixDeck.Models;

/// <summary>
/// The transition applied after a Local stateful button's command sequence has been triggered.
/// A transition is a behavior setting of the state — it is NOT a command.
/// </summary>
public enum StateTransitionKind
{
    /// <summary>Remain in the current state.</summary>
    Stay,

    /// <summary>Advance to the next state in the list (wraps around).</summary>
    Next,

    /// <summary>Go back to the previous state in the list (wraps around).</summary>
    Previous,

    /// <summary>Jump to a specific state identified by <see cref="StateTransition.TargetStateId"/>.</summary>
    Specific,

    /// <summary>Return to the button's default state.</summary>
    ResetToDefault
}
