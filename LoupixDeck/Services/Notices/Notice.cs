using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace LoupixDeck.Services.Notices;

/// <summary>
/// One message in the main window's notice strip: a short text, an optional action button and,
/// when the producer allows it, a dismiss button. Immutable once posted; a producer changes a
/// notice by posting a new one with the same <see cref="Id"/>.
/// </summary>
public sealed class Notice
{
    /// <summary>Stable key. Posting a notice with the id of one already shown replaces it in place.</summary>
    public required string Id { get; init; }

    /// <summary>The already translated text. Kept short: the strip must not widen the window.</summary>
    public required string Message { get; init; }

    /// <summary>Material Design Icons glyph shown in front of the text, or null for none.</summary>
    public string Icon { get; init; }

    /// <summary>Caption of the action button; the button is hidden when null.</summary>
    public string ActionText { get; init; }

    /// <summary>Runs when the action button is pressed.</summary>
    public ICommand ActionCommand { get; init; }

    /// <summary>Whether the user may close the notice. Dismissal is only offered when the producer
    /// can remember it (see <see cref="OnDismissed"/>); a notice that clears itself with the
    /// state it reports leaves this off.</summary>
    public bool IsDismissable { get; init; }

    /// <summary>Called after the user dismissed the notice, so the producer can remember it.</summary>
    public Action OnDismissed { get; init; }

    public bool HasIcon => !string.IsNullOrEmpty(Icon);

    public bool HasAction => !string.IsNullOrEmpty(ActionText) && ActionCommand != null;

    /// <summary>Bound by the dismiss button. Wired up by <see cref="INoticeService"/> when posted.</summary>
    public ICommand DismissCommand { get; internal set; } = new RelayCommand(() => { });
}
