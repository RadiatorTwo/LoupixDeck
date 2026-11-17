namespace LoupixDeck.Utils;

/// <summary>
/// The symbols and pack icons last chosen in the symbol picker, newest first. Kept in the global
/// <c>ui-settings.json</c>, never in a device config: the list is the same for every deck and is
/// not part of a profile.
/// </summary>
public static class RecentSymbols
{
    /// <summary>Entries kept; four rows of the picker grid.</summary>
    public const int MaxEntries = 24;

    private const string SettingKey = "symbolPickerRecent";

    /// <summary>Picker keys: symbol ids or pack icon keys (see <c>IconPackKey</c>).</summary>
    public static IReadOnlyList<string> Keys => UiSettingsStore.GetStringList(SettingKey);

    /// <summary>Moves <paramref name="key"/> to the front of the list.</summary>
    public static void Record(string key)
    {
        if (string.IsNullOrEmpty(key))
            return;

        UiSettingsStore.SetStringList(SettingKey, Keys
            .Where(k => !string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
            .Prepend(key)
            .Take(MaxEntries));
    }
}
