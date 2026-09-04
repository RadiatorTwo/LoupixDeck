namespace LoupixDeck.ViewModels;

/// <summary>
/// Raised by a button settings view model when the command that owned the button's states was
/// removed or replaced: the view asks the user whether the generated states are kept, then calls
/// back into <c>CompleteStateRelease</c>. Nothing changes until it does.
/// </summary>
public sealed class StateReleaseRequest(string ownerDisplayName) : EventArgs
{
    /// <summary>Display name of the command that owned the states.</summary>
    public string OwnerDisplayName { get; } = ownerDisplayName;
}
