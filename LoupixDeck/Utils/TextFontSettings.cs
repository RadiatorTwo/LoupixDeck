namespace LoupixDeck.Utils;

/// <summary>
/// The global default font for new text layers. It is copied into a layer's
/// <see cref="Models.Layers.TextLayer.FontFamily"/> when the layer is created, so changing it
/// later leaves existing layers alone, and a profile renders the same whatever this is set to on
/// the machine showing it. Global like the theme, not part of a device's config.
/// </summary>
public static class TextFontSettings
{
    private const string Key = "DefaultTextFontFamily";

    private static string _defaultFamily;
    private static bool _loaded;

    /// <summary>The family new text layers start with; null means the renderer's default.</summary>
    public static string DefaultFamily
    {
        get
        {
            if (!_loaded)
            {
                _defaultFamily = UiSettingsStore.GetString(Key);
                _loaded = true;
            }

            return _defaultFamily;
        }
        set
        {
            value = string.IsNullOrEmpty(value) ? null : value;
            if (_loaded && _defaultFamily == value) return;
            _defaultFamily = value;
            _loaded = true;
            UiSettingsStore.Set(Key, value);
        }
    }
}