using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using SkiaSharp;

namespace LoupixDeck.Utils;

/// <summary>
/// Measures and draws a single line of text with per-character font fallback.
/// <see cref="SKCanvas.DrawText(string, float, float, SKTextAlign, SKFont, SKPaint)"/> uses only
/// the font's own typeface, so a character it has no glyph for (an emoji such as 🔇 in
/// Liberation Sans) renders as nothing. Here each such character is drawn in the typeface the
/// system font manager matches for it, while the rest of the line keeps the chosen font.
/// </summary>
internal static class FallbackText
{
    /// <summary>
    /// Fallback typefaces keyed by (primary family, weight, slant, code point). Matching asks
    /// the system font manager and allocates a native typeface, so it runs once per character.
    /// Typefaces are long-lived and intentionally never disposed (see BitmapHelper's
    /// typeface cache). A null value records that no installed font has the character.
    /// </summary>
    private static readonly ConcurrentDictionary<(string Family, int Weight, SKFontStyleSlant Slant, int CodePoint), SKTypeface>
        FallbackCache = new();

    /// <summary>Advance width of <paramref name="text"/>, counting fallback glyphs.</summary>
    public static float Measure(SKFont font, ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
            return 0f;

        if (font.ContainsGlyphs(text))
            return font.MeasureText(text);

        float width = 0f;
        foreach ((SKTypeface typeface, int start, int length) in Runs(font, text))
            width += MeasureRun(font, typeface, text.Slice(start, length));
        return width;
    }

    /// <summary>
    /// Draws <paramref name="text"/> left-aligned with its baseline at
    /// (<paramref name="x"/>, <paramref name="y"/>); fallback runs share the primary baseline.
    /// </summary>
    public static void Draw(SKCanvas canvas, string text, float x, float y, SKFont font, SKPaint paint)
    {
        if (string.IsNullOrEmpty(text))
            return;

        if (font.ContainsGlyphs(text))
        {
            canvas.DrawText(text, x, y, SKTextAlign.Left, font, paint);
            return;
        }

        ReadOnlySpan<char> span = text.AsSpan();
        foreach ((SKTypeface typeface, int start, int length) in Runs(font, span))
        {
            string run = text.Substring(start, length);
            if (ReferenceEquals(typeface, font.Typeface))
            {
                canvas.DrawText(run, x, y, SKTextAlign.Left, font, paint);
                x += font.MeasureText(run);
                continue;
            }

            using SKFont runFont = CloneWith(font, typeface);
            canvas.DrawText(run, x, y, SKTextAlign.Left, runFont, paint);
            x += runFont.MeasureText(run);
        }
    }

    private static float MeasureRun(SKFont font, SKTypeface typeface, ReadOnlySpan<char> run)
    {
        if (ReferenceEquals(typeface, font.Typeface))
            return font.MeasureText(run);

        using SKFont runFont = CloneWith(font, typeface);
        return runFont.MeasureText(run);
    }

    private static SKFont CloneWith(SKFont font, SKTypeface typeface) => new(typeface, font.Size)
    {
        Edging = font.Edging,
        Subpixel = font.Subpixel,
        Hinting = font.Hinting
    };

    /// <summary>
    /// Splits <paramref name="text"/> into runs that each render in one typeface. Joiners,
    /// variation selectors and combining marks stay with the character before them, so an
    /// emoji sequence is not torn apart across fonts.
    /// </summary>
    private static List<(SKTypeface Typeface, int Start, int Length)> Runs(SKFont font, ReadOnlySpan<char> text)
    {
        List<(SKTypeface, int, int)> runs = [];
        SKTypeface primary = font.Typeface;
        SKTypeface current = null;
        int runStart = 0;
        int index = 0;

        while (index < text.Length)
        {
            Rune.DecodeFromUtf16(text[index..], out Rune rune, out int consumed);

            SKTypeface typeface = current != null && Attaches(rune)
                ? current
                : TypefaceFor(font, rune.Value);

            if (!ReferenceEquals(typeface, current))
            {
                if (current != null)
                    runs.Add((current, runStart, index - runStart));
                current = typeface;
                runStart = index;
            }

            index += consumed;
        }

        if (current != null)
            runs.Add((current, runStart, index - runStart));
        return runs;
    }

    private static bool Attaches(Rune rune)
    {
        if (rune.Value is 0x200D or (>= 0xFE00 and <= 0xFE0F) or (>= 0xE0020 and <= 0xE007F)
            or (>= 0x1F3FB and <= 0x1F3FF))
            return true;

        UnicodeCategory category = Rune.GetUnicodeCategory(rune);
        return category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark
            or UnicodeCategory.SpacingCombiningMark;
    }

    private static SKTypeface TypefaceFor(SKFont font, int codePoint)
    {
        SKTypeface primary = font.Typeface;
        if (primary == null || codePoint < 0x80 || font.ContainsGlyph(codePoint))
            return primary;

        SKFontStyle style = primary.FontStyle;
        SKTypeface fallback = FallbackCache.GetOrAdd(
            (primary.FamilyName, style.Weight, style.Slant, codePoint),
            key => SKFontManager.Default.MatchCharacter(
                key.Family, (SKFontStyleWeight)key.Weight, SKFontStyleWidth.Normal, key.Slant, null, key.CodePoint));

        return fallback ?? primary;
    }
}
