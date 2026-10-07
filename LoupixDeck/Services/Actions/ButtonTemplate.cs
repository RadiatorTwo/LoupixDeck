namespace LoupixDeck.Services.Actions;

/// <summary>
/// How the icon and the caption of a touch button are arranged on the key; see
/// <see cref="ActionAssignment.ApplyTemplate"/>.
/// </summary>
public enum ButtonTemplate
{
    IconCaptionBottom = 0,
    IconCaptionTop = 1,
    IconOnly = 2,
    TextOnly = 3
}