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

    /// <summary>Returns the parameter values inside the segment's parentheses, split on
    /// ',' and trimmed. Empty array when the segment has no parameter list.</summary>
    public static string[] GetParameters(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
            return [];

        ReadOnlySpan<char> span = segment.AsSpan();
        int start = span.IndexOf('(');
        int end = span.IndexOf(')');
        if (start < 0 || end < 0 || end <= start)
            return [];

        ReadOnlySpan<char> inner = span.Slice(start + 1, end - start - 1);
        int count = 0;
        foreach (Range range in inner.Split(','))
        {
            if (!inner[range].Trim().IsEmpty)
                count++;
        }

        if (count == 0)
            return [];

        string[] result = new string[count];
        int i = 0;
        foreach (Range range in inner.Split(','))
        {
            ReadOnlySpan<char> piece = inner[range].Trim();
            if (piece.IsEmpty)
                continue;
            result[i++] = piece.ToString();
        }

        return result;
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
