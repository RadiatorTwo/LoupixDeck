using LoupixDeck.PluginSdk;

namespace LoupixDeck.Utils;

/// <summary>
/// Shared parsing for chained command strings (e.g. <c>"a(x) &amp;&amp; b(y,z)"</c>).
/// Single source of truth so the command executor (<c>CommandService</c>) and the
/// touch-button command editor split and dissect commands identically — they must
/// not drift apart, or a string the editor builds could be executed differently.
/// </summary>
public static class CommandStringParser
{
    /// <summary>Splits a chained command into its individual segments (already trimmed,
    /// empty segments removed).</summary>
    public static IEnumerable<string> SplitChain(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return [];

        List<string> parts = [];
        ReadOnlySpan<char> span = command.AsSpan();
        int start = 0;
        while (true)
        {
            int relative = span.Slice(start).IndexOf("&&");
            ReadOnlySpan<char> part = relative < 0
                ? span.Slice(start)
                : span.Slice(start, relative);
            part = part.Trim();
            if (!part.IsEmpty)
                parts.Add(part.ToString());
            if (relative < 0)
                return parts;
            start += relative + 2;
        }
    }

    /// <summary>Returns the command name of a single segment — everything before the
    /// opening parenthesis (or the whole segment when it has no parameter list).</summary>
    public static string GetName(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
            return string.Empty;

        ReadOnlySpan<char> span = segment.AsSpan();
        int open = span.IndexOf('(');
        ReadOnlySpan<char> name = open < 0 ? span.Trim() : span.Slice(0, open).Trim();
        return name.ToString();
    }

    /// <summary>
    /// Returns the index of the <c>')'</c> that closes the parameter list opened at
    /// <paramref name="open"/>, or <c>-1</c> when there is none.
    /// </summary>
    /// <remarks>
    /// Balanced parentheses inside a value are skipped, so <c>SetScene(Scene (1))</c> yields
    /// <c>Scene (1)</c> instead of being cut at the first <c>')'</c>. When the value's parentheses
    /// do not balance, this falls back to the first <c>')'</c> after <paramref name="open"/>, which
    /// is exactly what the parser did before. A result therefore only differs from the old one
    /// where the old one had cut a value apart. The depth search stops at the matching
    /// <c>')'</c>, so a whole chain (<c>a(x) &amp;&amp; b(y)</c>) still ends after <c>x</c>.
    /// </remarks>
    public static int FindParameterListEnd(ReadOnlySpan<char> text, int open)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '(')
            {
                depth++;
            }
            else if (text[i] == ')')
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }

        int first = text.Slice(open).IndexOf(')');
        return first < 0 ? -1 : open + first;
    }

    /// <summary>Returns the parameter values inside the segment's parentheses, split on
    /// ',' and trimmed. Empty array when the segment has no parameter list.</summary>
    /// <remarks>
    /// An empty value keeps its position (<c>a(,x)</c> → <c>["", "x"]</c>): parameters are
    /// positional, so dropping it would shift every later value onto the wrong parameter.
    /// Trailing empty values are dropped, as they always were, so <c>a(x,)</c> stays
    /// <c>["x"]</c> and commands checking the parameter count see what they did before.
    /// The list ends at the matching <c>')'</c> (see <see cref="FindParameterListEnd"/>).
    /// </remarks>
    public static string[] GetParameters(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
            return [];

        ReadOnlySpan<char> span = segment.AsSpan();
        int start = span.IndexOf('(');
        if (start < 0)
            return [];

        int end = FindParameterListEnd(span, start);
        if (end < 0)
            return [];

        ReadOnlySpan<char> inner = span.Slice(start + 1, end - start - 1);
        List<string> result = [];
        foreach (Range range in inner.Split(','))
            result.Add(inner[range].Trim().ToString());

        int count = result.Count;
        while ((count > 0) && (result[count - 1].Length == 0))
            count--;

        return result.GetRange(0, count).ToArray();
    }

    /// <summary>
    /// Builds the ordered sibling list handed to a display command via
    /// <see cref="CommandContext.SequenceCommands"/>. Returns an empty list for a
    /// single-command button (no <c>&amp;&amp;</c> chain) so a plugin can treat "in a
    /// sequence" as a distinct signal; otherwise one entry per segment, in order.
    /// The 4-command tile cap is intentionally left to the plugin — all segments flow through.
    /// </summary>
    public static IReadOnlyList<SequenceCommand> BuildSequence(string command)
    {
        List<string> segments = SplitChain(command).ToList();
        if (segments.Count < 2)
            return [];

        return segments.Select(segment => new SequenceCommand(GetName(segment), GetParameters(segment))).ToList();
    }
}
