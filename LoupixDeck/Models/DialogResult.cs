namespace LoupixDeck.Models;

public class DialogResult(bool isConfirmed)
{
    public bool IsConfirmed { get; } = isConfirmed;

    public static DialogResult Ok() => new(true);
    public static DialogResult Cancel() => new(false);
}