using System.Globalization;

namespace LoupixDeck.Localization;

/// <summary>
/// Shorthand for translating text produced in C# rather than in XAML, where the
/// <see cref="TrExtension"/> markup extension is not available.
///
/// Unlike a XAML binding this resolves once, at the moment the text is produced, so a dialog
/// message or a status line keeps the wording it was built with until it is recomputed. That is
/// deliberate: these strings are short-lived, and an observable wrapper would cost far more than
/// it returns.
/// </summary>
public static class Loc
{
    /// <summary>The translated string for <paramref name="key"/>.</summary>
    public static string Tr(string key) => LocalizationManager.Instance[key];

    /// <summary>
    /// The translated composite format string for <paramref name="key"/>, filled with
    /// <paramref name="args"/>. Falls back to the unfilled template when the translation carries
    /// the wrong placeholders, so a bad dictionary entry cannot throw in front of the user.
    /// </summary>
    public static string Tr(string key, params object[] args)
    {
        string template = LocalizationManager.Instance[key];

        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args);
        }
        catch (FormatException ex)
        {
            Console.WriteLine($"[Localization] Template '{key}' does not match its arguments: {ex.Message}");
            return template;
        }
    }
}
