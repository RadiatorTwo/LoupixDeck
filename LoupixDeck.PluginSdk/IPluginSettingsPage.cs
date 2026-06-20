namespace LoupixDeck.PluginSdk;

/// <summary>
/// Optional capability a <see cref="LoupixPlugin"/> implements to expose
/// user-editable settings. The host renders the <see cref="SettingsSchema"/>
/// generically, persists values into the plugin's <see cref="IPluginSettings"/>,
/// and calls <see cref="OnSettingsSaved"/> afterwards so the plugin can apply
/// them (reconnect, restart polling, …).
/// </summary>
public interface IPluginSettingsPage
{
    /// <summary>The editable settings, in display order.</summary>
    IReadOnlyList<PluginSettingDescriptor> SettingsSchema { get; }

    /// <summary>Optional action buttons shown on the settings form.</summary>
    IReadOnlyList<PluginSettingAction> SettingsActions { get; }

    /// <summary>
    /// Called by the host after the user's edited values have been written to
    /// <see cref="IPluginSettings"/> and saved.
    /// </summary>
    void OnSettingsSaved();
}
