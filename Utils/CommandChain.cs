namespace LoupixDeck.Utils;

public static class CommandChain
{
    /// <summary>
    /// Appends <paramref name="addition"/> to <paramref name="existing"/> with
    /// " &amp;&amp; " between them. If <paramref name="existing"/> is empty,
    /// returns <paramref name="addition"/> verbatim; if it already ends with
    /// "&amp;&amp;" the separator is not doubled.
    /// </summary>
    public static string Append(string existing, string addition)
    {
        if (string.IsNullOrEmpty(addition)) return existing ?? string.Empty;
        if (string.IsNullOrWhiteSpace(existing)) return addition;
        var trimmed = existing.TrimEnd();
        return trimmed.EndsWith("&&") ? existing + " " + addition : existing + " && " + addition;
    }
}
