using CommunityToolkit.Mvvm.ComponentModel;

namespace LoupixDeck.Models;

/// <summary>
/// The behavior applied to a Local stateful button after its command sequence runs:
/// which state becomes active next. A transition is a dedicated behavior setting of a
/// state, not a command — there is no on-success / on-failure handling.
/// </summary>
public partial class StateTransition : ObservableObject
{
    [ObservableProperty]
    public partial StateTransitionKind Kind { get; set; } = StateTransitionKind.Stay;

    /// <summary>Target state for <see cref="StateTransitionKind.Specific"/>; ignored otherwise.</summary>
    [ObservableProperty]
    public partial Guid? TargetStateId { get; set; }
}
