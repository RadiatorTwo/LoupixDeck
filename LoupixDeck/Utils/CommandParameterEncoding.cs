namespace LoupixDeck.Utils;

/// <summary>
/// Escapes the characters a command parameter may not contain literally, so values such as
/// filesystem paths and launcher URIs survive a round trip through
/// <see cref="CommandStringParser"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CommandStringParser.GetParameters"/> slices a segment between its first <c>(</c> and
/// its <b>first</b> <c>)</c>, then splits the result on <c>,</c> and trims each piece, while
/// <see cref="CommandStringParser.SplitChain"/> splits a chain on <c>&amp;&amp;</c>. A raw Windows
/// path therefore breaks in three separate ways — most commonly
/// <c>C:\Program Files (x86)\…</c>, which truncates at the <c>)</c> of <c>(x86)</c>.
/// </para>
/// <para>
/// Only the five characters that actually break parsing are escaped, so an encoded value stays
/// close to readable: <c>C:\Program Files %28x86%29\Steam\steam.exe</c>. Anything else — spaces,
/// backslashes, colons — is left alone.
/// </para>
/// <para>
/// <c>%</c> is escaped first on the way in and unescaped last on the way out. That ordering is what
/// makes the round trip lossless: every <c>%</c> surviving in an encoded value is one this class
/// wrote, so a literal <c>%28</c> in the source becomes <c>%2528</c> and cannot be mistaken for an
/// escaped <c>(</c>.
/// </para>
/// </remarks>
public static class CommandParameterEncoding
{
    private const string PercentEscape = "%25";
    private const string OpenParenEscape = "%28";
    private const string CloseParenEscape = "%29";
    private const string CommaEscape = "%2C";
    private const string AmpersandEscape = "%26";

    /// <summary>
    /// Escapes <paramref name="value"/> for storage inside a command's parameter list. Returns the
    /// input unchanged when it is null or contains nothing that needs escaping.
    /// </summary>
    public static string Encode(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        // '%' must go first: it introduces every other escape, so escaping it afterwards would
        // also mangle the ones written below.
        return value
            .Replace("%", PercentEscape)
            .Replace("(", OpenParenEscape)
            .Replace(")", CloseParenEscape)
            .Replace(",", CommaEscape)
            .Replace("&", AmpersandEscape);
    }

    /// <summary>
    /// Reverses <see cref="Encode"/>. A value that was never encoded passes through unchanged, so
    /// this is safe to apply to a parameter written before the encoding existed.
    /// </summary>
    public static string Decode(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        // '%' must go last, mirroring Encode.
        return value
            .Replace(OpenParenEscape, "(")
            .Replace(CloseParenEscape, ")")
            .Replace(CommaEscape, ",")
            .Replace(AmpersandEscape, "&")
            .Replace(PercentEscape, "%");
    }
}