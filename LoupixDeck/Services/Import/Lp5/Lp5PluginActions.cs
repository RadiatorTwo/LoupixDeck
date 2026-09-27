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

    /// <summary>Dial turns whose adjustment carries a parameter after a fixed prefix.</summary>
    private static readonly (string Prefix, string Command)[] ParameterizedAdjustments =
    [
        // Both address the output by its Windows endpoint id.
        ("$VolumeControl___Loupedeck.VolumeControlPlugin.Commands.OutputAdjustment___", "Audio.Volume")
    ];

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

            case "$AudioControl___Loupedeck.AudioControlPlugin.AudioControlVolumeAdjustment":
                // Used both as the dial turn and as its press ("reset"); Audio.AppVolume mutes on a press.
                string app = parameter("type") == "application" ? AppId(parameter("endpoint")) : null;
                return app != null ? $"Audio.AppVolume({CommandParameterEncoding.Encode(app)})" : null;

            default:
                return null;
        }
    }

    /// <summary>
    /// The Audio plugin's app id for an AudioControl endpoint: <c>foregroundApplication</c>, or
    /// <c>&lt;device id&gt;|&lt;exe path&gt;%b&lt;guid&gt;</c>, whose executable name, lower-cased and
    /// without extension, is what the Audio plugin identifies apps by.
    /// </summary>
    private static string AppId(string endpoint)
    {
        if (string.IsNullOrEmpty(endpoint)) return null;
        if (endpoint == "foregroundApplication") return "@foreground";

        int bar = endpoint.IndexOf('|');
        if (bar < 0) return null;

        string path = endpoint[(bar + 1)..];
        int suffix = path.IndexOf("%b", StringComparison.Ordinal);
        if (suffix >= 0) path = path[..suffix];

        // Windows paths: take the last segment by hand, Path.GetFileName ignores '\' off Windows.
        string file = path[(path.LastIndexOf('\\') + 1)..];
        string name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
        return name.Length > 0 ? name : null;
    }

    /// <summary>The adjustment command for a Loupedeck plugin dial turn, or null when none is known.</summary>
    public static string Adjustment(string rotateRef)
    {
        if (Adjustments.TryGetValue(rotateRef, out string command))
            return command;

        foreach ((string prefix, string name) in ParameterizedAdjustments)
        {
            if (rotateRef.Length > prefix.Length && rotateRef.StartsWith(prefix, StringComparison.Ordinal))
                return $"{name}({CommandParameterEncoding.Encode(rotateRef[prefix.Length..])})";
        }

        return null;
    }
}
