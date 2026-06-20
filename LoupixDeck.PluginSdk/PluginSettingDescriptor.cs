namespace LoupixDeck.PluginSdk;

/// <summary>
/// Describes one user-editable plugin setting. The host renders an editor for
/// it generically (no UI code in the plugin) and persists the value into the
/// plugin's <see cref="IPluginSettings"/> under <see cref="Key"/>.
/// </summary>
public sealed class PluginSettingDescriptor
{
    /// <summary>Storage key in <see cref="IPluginSettings"/>.</summary>
    public required string Key { get; init; }

    /// <summary>Field label shown in the settings form.</summary>
    public required string Label { get; init; }

    /// <summary>Editor kind / stored value type.</summary>
    public PluginSettingKind Kind { get; init; } = PluginSettingKind.Text;

    /// <summary>Optional helper text shown beneath the field.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Value used when the key is absent from the settings store.</summary>
    public object DefaultValue { get; init; }
}
