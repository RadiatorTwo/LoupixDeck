using LoupixDeck.Models;
namespace LoupixDeck.Utils;

public static class CommandChain
{
    /// <summary>
    /// Appends the command string <paramref name="addition"/> to <paramref name="existing"/>. If
    /// <paramref name="existing"/> is empty, the result is <paramref name="addition"/> alone.
    /// </summary>
    public static CommandBinding Append(CommandBinding existing, string addition)
    {
        if (string.IsNullOrEmpty(addition)) return existing ?? CommandBinding.Empty;
        if (CommandBinding.IsNullOrEmpty(existing)) return addition;
        return existing.Append(CommandBinding.FromLegacy(addition));
    }
}
