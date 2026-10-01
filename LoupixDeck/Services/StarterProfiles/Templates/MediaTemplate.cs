using LoupixDeck.Models;
using static LoupixDeck.Services.StarterProfiles.StarterCommands;

namespace LoupixDeck.Services.StarterProfiles.Templates;

/// <summary>
/// Media playback with the track on the keys. Windows reads it from the system media session
/// (Media Session plugin), Linux from MPRIS (MPRIS Media plugin).
/// </summary>
internal sealed class MediaTemplate : StarterProfileTemplate
{
    private static readonly StarterPluginRequirement MediaSession = new("mediasession", "Media Session");
    private static readonly StarterPluginRequirement Mpris = new("mpris", "MPRIS Media");

    public override string Id => "media";
    protected override string NameKey => "StarterProfile_Media_Name";
    protected override string DescriptionKey => "StarterProfile_Media_Description";
    public override string SymbolId => "music";

    public override IReadOnlyList<StarterPluginRequirement> GetRequiredPlugins(bool isWindows) =>
        [isWindows ? MediaSession : Mpris];

    public override void Build(StarterProfileBuilder b, Profile profile)
    {
        Workspace workspace = b.AddWorkspace(profile, "Media");
        TouchButtonPage page = b.AddTouchPage(workspace, "Player");

        if (b.IsWindows)
            BuildWindows(b, workspace, page);
        else
            BuildLinux(b, workspace, page);
    }

    private static void BuildWindows(StarterProfileBuilder b, Workspace workspace, TouchButtonPage page)
    {
        // Pressing the now-playing key toggles playback.
        b.Display(page, 0, 0, "MediaSession.NowPlaying");
        b.Key(page, 0, 1, "MediaSession.Previous", "Previous", "skip-previous");
        b.Key(page, 0, 2, PlayPause, "Play/Pause", "play-pause");
        b.Key(page, 0, 3, "MediaSession.Next", "Next", "skip-next");
        b.Key(page, 0, 4, Keys("MediaStop"), "Stop", "stop");

        AddVolumeRow(b, page);

        b.AddDialPage(workspace, "Player", VolumeDial, TrackDial);
    }

    private static void BuildLinux(StarterProfileBuilder b, Workspace workspace, TouchButtonPage page)
    {
        // "auto" follows whichever player is active; pressing a display key toggles playback.
        b.Display(page, 0, 0, "Mpris.Artwork(auto)");
        b.Key(page, 0, 1, "Mpris.Previous(auto)", "Previous", "skip-previous");
        b.Key(page, 0, 2, "Mpris.PlayPause(auto)", "Play/Pause", "play-pause");
        b.Key(page, 0, 3, "Mpris.Next(auto)", "Next", "skip-next");
        b.Display(page, 0, 4, "Mpris.NowPlaying(auto)");

        b.Key(page, 1, 0, "Mpris.SeekBackward(auto,,10)", "-10 s", "rewind");
        b.Key(page, 1, 1, "Mpris.SeekForward(auto,,10)", "+10 s", "fast-forward");
        b.Key(page, 1, 2, "Mpris.ToggleShuffle(auto)", "Shuffle", "shuffle");
        b.Key(page, 1, 3, "Mpris.CycleRepeat(auto)", "Repeat", "repeat");
        b.Key(page, 1, 4, "Mpris.SelectPlayer", "Player", "speaker");

        AddVolumeRow(b, page, row: 2);

        b.AddDialPage(workspace, "Player",
            VolumeDial,
            new StarterDial("Seek", "Mpris.SeekBackward(auto,,5)", "Mpris.SeekForward(auto,,5)", "Mpris.PlayPause(auto)"),
            new StarterDial("Track", "Mpris.Previous(auto)", "Mpris.Next(auto)", "Mpris.PlayPause(auto)"),
            new StarterDial("Player volume", "Mpris.VolumeDown(auto,,5)", "Mpris.VolumeUp(auto,,5)"));
    }

    /// <summary>System volume keys, centred in the row.</summary>
    private static void AddVolumeRow(StarterProfileBuilder b, TouchButtonPage page, int row = 1)
    {
        b.Key(page, row, 1, VolumeDown, "Volume -", "volume-minus");
        b.Key(page, row, 2, Mute, "Mute", "volume-off");
        b.Key(page, row, 3, VolumeUp, "Volume +", "volume-plus");
    }
}