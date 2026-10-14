using Avalonia;
using Avalonia.Styling;

namespace LoupixDeck.Utils;

/// <summary>
/// The app theme ("Dark", "Light" or "System"). It applies to the whole app, so it lives in the
/// device-independent <c>ui-settings.json</c>. It used to be kept in each device's config, where
/// the start-up read the primary device's value while Settings showed and changed the selected
/// device's, so the two drifted apart with more than one device.
/// </summary>
public static class ThemeSettings
{
    private const string Key = "Theme";

    /// <summary>The theme in effect, set by <see cref="Initialize"/> and <see cref="Apply"/>.</summary>
    public static string Current { get; private set; } = "Dark";

    /// <summary>
    /// Loads the saved theme and applies it. Until one is saved here, the primary device's old
    /// per-device value is used, which is what the start-up applied before, so an existing setup
    /// opens unchanged.
    /// </summary>
    public static void Initialize(string legacyValue)
    {
        Current = UiSettingsStore.GetString(Key) ?? legacyValue ?? "Dark";
        SetRequestedVariant();
    }

    /// <summary>Switches the theme and saves it for every device.</summary>
    public static void Apply(string variant)
    {
        if (Current == variant) return;
        Current = variant;
        UiSettingsStore.Set(Key, variant);
        SetRequestedVariant();
    }

    private static void SetRequestedVariant()
    {
        Application.Current?.RequestedThemeVariant = Current switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }
}
