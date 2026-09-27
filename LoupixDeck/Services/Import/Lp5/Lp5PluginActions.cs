using LoupixDeck.Utils;

namespace LoupixDeck.Services.Import.Lp5;

/// <summary>
/// Maps actions of Loupedeck plugins to commands of LoupixDeck plugins. The command is imported
/// whether the plugin is installed or not; the import dialog lists the plugins that are missing.
/// </summary>
internal static class Lp5PluginActions
{
    private const string HomeAssistantByBatu = "$HomeAssistantByBatu___Loupedeck.HomeAssistantByBatuPlugin.";

    /// <summary>Actions without a parameter.</summary>
    private static readonly Dictionary<string, string> Commands = new(StringComparer.Ordinal)
    {
        // Dynamic folders: the LoupixDeck plugin builds the folder's content itself.
        ["$AudioSwitcher___#DynamicFolder___DynamicFolder#Loupedeck.Steinerd.AudioSwitcherPlugin.Actions.AudioDevicesFolder"]
            = "Audio.OutputDevices",
        ["$AudioControl___#DynamicFolder___DynamicFolder#Loupedeck.AudioControlPlugin.AudioRenderSessionsFolder"]
            = "Audio.Mixer",
        [HomeAssistantByBatu + "Commands.ConnectionStatusCommand"] = "HomeAssistant.ConnectionStatus",
        ["$OBSStudioForLogi___Loupedeck.OBSStudioForLogiPlugin.StudioModeTransitionCommand"] = "System.ObsTriggerTransition"
    };

    /// <summary>Actions that carry a parameter after a fixed prefix, e.g. an entity id.</summary>
    private static readonly (string Prefix, string Command)[] ParameterizedCommands =
    [
        (HomeAssistantByBatu + "Commands.ToggleEntityCommand___", "HomeAssistant.ToggleEntity"),
        (HomeAssistantByBatu + "Commands.SensorDisplayCommand___", "HomeAssistant.ShowEntity"),
        // A climate dial placed on a key: its controls folder offers the same temperature steps.
        ("$@Generic___@AdjustmentAsCommand___$HomeAssistantByBatu#¤%&+?Loupedeck.HomeAssistantByBatuPlugin.Adjustments.ClimateAdjustment#¤%&+?",
            "HomeAssistant.OpenEntityControls"),
        // Loupedeck stores the playlist URI; the Spotify API takes the bare id.
        ("$Spotify___SaveToPlaylist___spotify:playlist:", "SpotifyPremium.SaveToPlaylist")
    ];

    /// <summary>Dial turns taken over by one adjustment command, which handles both directions.</summary>
    private static readonly Dictionary<string, string> Adjustments = new(StringComparer.Ordinal)
    {
        ["$Spotify___SpotifyVolume"] = "SpotifyPremium.VolumeAdjustment"
    };

    /// <summary>The LoupixDeck command for a Loupedeck plugin action, or null when none is known.</summary>
    public static string Command(string actionRef)
    {
        if (Commands.TryGetValue(actionRef, out string command))
            return command;

        foreach ((string prefix, string name) in ParameterizedCommands)
        {
            if (actionRef.Length > prefix.Length && actionRef.StartsWith(prefix, StringComparison.Ordinal))
                return $"{name}({CommandParameterEncoding.Encode(actionRef[prefix.Length..])})";
        }

        return null;
    }

    /// <summary>
    /// The LoupixDeck command for a profile action built from a Loupedeck plugin template, or null when the
    /// template is unknown or uses options LoupixDeck cannot reproduce.
    /// </summary>
    public static string ProfileActionCommand(string template, Func<string, string> parameter)
    {
        switch (template)
        {
            case "$OBSStudioForLogi___SceneSwitchAdjustable":
                // Switching the OBS profile or scene collection along with the scene is not supported.
                string scene = parameter("sceneName");
                return !string.IsNullOrEmpty(scene) && string.IsNullOrEmpty(parameter("profileName"))
                                                    && string.IsNullOrEmpty(parameter("collectionName"))
                    ? $"System.ObsSetScene({CommandParameterEncoding.Encode(scene)})"
                    : null;

            default:
                return null;
        }
    }

    /// <summary>The adjustment command for a Loupedeck plugin dial turn, or null when none is known.</summary>
    public static string Adjustment(string rotateRef) => Adjustments.GetValueOrDefault(rotateRef);
}
