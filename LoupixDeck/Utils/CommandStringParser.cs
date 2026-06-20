using System.Text.RegularExpressions;

namespace LoupixDeck.Utils;

/// <summary>
/// Shared parsing for chained command strings (e.g. <c>"a(x) &amp;&amp; b(y,z)"</c>).
/// Single source of truth so the command executor (<c>CommandService</c>) and the
/// touch-button command editor split and dissect commands identically — they must
/// not drift apart, or a string the editor builds could be executed differently.
/// </summary>
public static class CommandStringParser
{
    // Splits on "&&" with any amount of surrounding whitespace, so both
    // "a && b" and "a&&b" are treated the same.
    private static readonly Regex ChainSplitter = new(@"\s*&&\s*", RegexOptions.Compiled);

    /// <summary>Splits a chained command into its individual segments (already trimmed,
    /// empty segments removed).</summary>
    public static IEnumerable<string> SplitChain(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            yield break;

        foreach (var part in ChainSplitter.Split(command))
        {
            if (string.IsNullOrWhiteSpace(part)) continue;
            yield return part.Trim();
        }
    }

    /// <summary>Returns the command name of a single segment — everything before the
    /// opening parenthesis (or the whole segment when it has no parameter list).</summary>
    public static string GetName(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
            return string.Empty;

        var open = segment.IndexOf('(');
        return open == -1 ? segment.Trim() : segment[..open].Trim();
    }

    /// <summary>Returns the parameter values inside the segment's parentheses, split on
    /// ',' and trimmed. Empty array when the segment has no parameter list.</summary>
    public static string[] GetParameters(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
            return [];

        var start = segment.IndexOf('(');
        var end = segment.IndexOf(')');
        if (start == -1 || end == -1 || end <= start)
            return [];

        var inner = segment.Substring(start + 1, end - start - 1);
        return inner.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
