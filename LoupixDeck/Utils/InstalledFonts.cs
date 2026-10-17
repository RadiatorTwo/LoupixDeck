using LoupixDeck.Localization;
using SkiaSharp;

namespace LoupixDeck.Utils;

/// <summary>One entry of a font picker. A null <see cref="Family"/> stands for the default font.</summary>
public sealed record FontChoice(string Family, string Title);

/// <summary>
/// The font families a text layer can be set to: the ones Skia, which draws the text, can see on
/// this machine. Read once, since enumerating them walks the system font collection.
/// </summary>
public static class InstalledFonts
{
    private static IReadOnlyList<FontChoice> _choices;

    /// <summary>"Default" first, then the installed families in alphabetical order.</summary>
    public static IReadOnlyList<FontChoice> Choices => _choices ??= Load();

    /// <summary>The entry for <paramref name="family"/>; empty means the default entry.</summary>
    public static FontChoice Find(string family)
    {
        if (string.IsNullOrEmpty(family))
            return Choices[0];
        // A family the file names but this machine lacks still shows as picked.
        return Choices.FirstOrDefault(c => string.Equals(c.Family, family, StringComparison.OrdinalIgnoreCase))
            ?? new FontChoice(family, family);
    }

    private static List<FontChoice> Load()
    {
        List<FontChoice> choices = [new FontChoice(null, Loc.Tr("Common_DefaultFont"))];
        choices.AddRange(SKFontManager.Default.FontFamilies
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.CurrentCultureIgnoreCase)
            .Select(f => new FontChoice(f, f)));
        return choices;
    }
}