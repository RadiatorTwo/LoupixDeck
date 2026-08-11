using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels;

/// <summary>
/// Lists every key name a key macro step accepts on this platform. The list is built from
/// <see cref="KeyNames.Catalog"/>, i.e. from the very tables the keyboard backends resolve
/// against, so it cannot drift from what actually works.
/// </summary>
public sealed class KeyReferenceViewModel : ViewModelBase
{
    public KeyReferenceViewModel()
    {
        Groups = KeyNames.Catalog
            .GroupBy(entry => entry.Category)
            .Select(group => new KeyReferenceGroup(
                CategoryTitle(group.Key),
                group.Select(entry => new KeyReferenceEntry(
                        KeyCaptureMap.GetDisplayName(entry.Name),
                        string.Join(", ", entry.Aliases)))
                    .ToList()))
            .ToList();
    }

    public IReadOnlyList<KeyReferenceGroup> Groups { get; }

    public string Intro { get; } =
        "Type these names into the Keys field, joined with '+' for a combination " +
        "(Ctrl+Shift+Esc). Names are case-insensitive; the spellings in brackets mean the " +
        "same key.";

    public string CharacterHint { get; } =
        "Characters are keys too: 'ü', 'ä', 'ß', '#', '+' or any other character your " +
        "keyboard types are resolved through the active keyboard layout, so they need no " +
        "name from this list. The names below identify a key by its physical place on the " +
        "board instead (US legend), which keeps a macro on the same key on every layout.";

    private static string CategoryTitle(KeyNameCategory category) => category switch
    {
        KeyNameCategory.Modifiers => "Modifiers",
        KeyNameCategory.Control => "Control keys",
        KeyNameCategory.Navigation => "Navigation",
        KeyNameCategory.FunctionKeys => "Function keys",
        KeyNameCategory.Letters => "Letters",
        KeyNameCategory.Digits => "Digits (number row)",
        KeyNameCategory.Punctuation => "Punctuation positions",
        KeyNameCategory.Keypad => "Numeric keypad",
        KeyNameCategory.System => "System keys",
        KeyNameCategory.Media => "Media and volume",
        KeyNameCategory.Browser => "Browser and launcher",
        _ => "Other"
    };
}

/// <summary>One category of key names as shown in the reference window.</summary>
public sealed record KeyReferenceGroup(string Title, IReadOnlyList<KeyReferenceEntry> Keys)
{
    /// <summary>
    /// The group's body. Names without aliases (letters, digits, function keys) read fine as
    /// one wrapped line; where aliases exist each key gets its own line so they stay legible.
    /// </summary>
    public string Text => Keys.All(key => string.IsNullOrEmpty(key.Aliases))
        ? string.Join(", ", Keys.Select(key => key.Name))
        : string.Join(Environment.NewLine, Keys.Select(key => key.Text));
}

/// <summary>A single key name plus its alias spellings, ready for display.</summary>
public sealed record KeyReferenceEntry(string Name, string Aliases)
{
    /// <summary>The line as shown: "Ctrl [control, ctl, strg]".</summary>
    public string Text => string.IsNullOrEmpty(Aliases) ? Name : $"{Name}  [{Aliases}]";
}
