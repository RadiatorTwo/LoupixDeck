namespace LoupixDeck.ViewModels;

/// <summary>
/// Which pager a page-name field belongs to, so its context menu copies and pastes the right page
/// (issue #621). Set as the field's <c>Tag</c>.
/// </summary>
public enum PageSlot
{
    Touch,

    /// <summary>The shared rotary pages of a device that pages all its dials together.</summary>
    Rotary,

    LeftRotary,
    RightRotary
}
