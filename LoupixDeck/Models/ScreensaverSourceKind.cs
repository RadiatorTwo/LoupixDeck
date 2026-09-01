namespace LoupixDeck.Models;

/// <summary>
/// Which source drives the idle screensaver (issue #124). Serialized as its numeric value, so a
/// config saved before the plugin source existed simply has no entry and falls back to
/// <see cref="Video"/> — the original behavior.
/// </summary>
public enum ScreensaverSourceKind
{
    /// <summary>The configured video/GIF clip, decoded via ffmpeg. The default and the only
    /// behavior before SDK 1.20.0.</summary>
    Video = 0,

    /// <summary>A plugin-provided full-display renderer selected by id. Needs no ffmpeg.</summary>
    Plugin = 1
}
