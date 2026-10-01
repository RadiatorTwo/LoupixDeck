namespace LoupixDeck.Services.StarterProfiles;

/// <summary>Command strings the starter profiles share.</summary>
internal static class StarterCommands
{
    public static string Keys(string combination) => $"System.KeyCombination({combination})";

    public static string Url(string url) => $"System.OpenUrl({url})";

    public static string Workspace(Guid id) => $"System.GotoWorkspace({id})";

    public const string Clock = "DynamicText.Clock(HH:mm)";

    public static readonly string PlayPause = Keys("PlayPause");
    public static readonly string PreviousTrack = Keys("PrevTrack");
    public static readonly string NextTrack = Keys("NextTrack");
    public static readonly string Mute = Keys("Mute");
    public static readonly string VolumeDown = Keys("VolumeDown");
    public static readonly string VolumeUp = Keys("VolumeUp");

    /// <summary>Redo: Ctrl+Y is the Windows convention, Ctrl+Shift+Z the Linux one.</summary>
    public static string Redo(bool isWindows) => Keys(isWindows ? "Ctrl+Y" : "Ctrl+Shift+Z");

    public const string ProjectUrl = "https://github.com/RadiatorTwo/LoupixDeck";

    /// <summary>The system volume on a dial: turn to change it, press to mute.</summary>
    public static StarterDial VolumeDial => new("Volume", VolumeDown, VolumeUp, Mute);

    /// <summary>Track skipping on a dial: turn to skip, press to play or pause.</summary>
    public static StarterDial TrackDial => new("Track", PreviousTrack, NextTrack, PlayPause);
}