using Avalonia.Media;
using LoupixDeck.Models;
using static LoupixDeck.Services.StarterProfiles.StarterCommands;

namespace LoupixDeck.Services.StarterProfiles.Templates;

/// <summary>
/// OBS Studio control through the OBS Studio plugin: recording and streaming with live state,
/// scenes, audio mutes and the replay buffer.
/// </summary>
internal sealed class ObsTemplate : StarterProfileTemplate
{
    private static readonly StarterPluginRequirement Obs = new("obs", "OBS Studio");

    private static readonly Color Idle = Color.Parse("#2B2F36");
    private static readonly Color Active = Color.Parse("#B3261E");
    private static readonly Color Enabled = Color.Parse("#1F6E43");

    public override string Id => "obs";
    protected override string NameKey => "StarterProfile_Obs_Name";
    protected override string DescriptionKey => "StarterProfile_Obs_Description";
    protected override string SetupNoteKey => "StarterProfile_Obs_SetupNote";
    public override string SymbolId => "broadcast";

    public override IReadOnlyList<StarterPluginRequirement> GetRequiredPlugins(bool isWindows) => [Obs];

    public override void Build(StarterProfileBuilder b, Profile profile)
    {
        Workspace workspace = b.AddWorkspace(profile, "OBS");
        TouchButtonPage page = b.AddTouchPage(workspace, "Control");

        // The state names are the ones the plugin declares and reports, so it switches them live.
        b.Toggle(page, 0, 0, "System.ObsToggleRecord",
            new StarterState("Idle", "Record", "record-rec", Idle),
            new StarterState("Recording", "Recording", "record-rec", Active), ownedByCommand: true);
        b.Toggle(page, 0, 1, "System.ObsToggleStream",
            new StarterState("Offline", "Go live", "broadcast", Idle),
            new StarterState("Live", "Live", "broadcast", Active), ownedByCommand: true);
        b.Key(page, 0, 2, "System.ObsPauseRecord", "Pause rec", "pause");
        b.Toggle(page, 0, 3, "System.ObsVirtualCam",
            new StarterState("Off", "Virtual cam", "webcam", Idle),
            new StarterState("On", "Virtual cam", "webcam", Enabled), ownedByCommand: true);
        b.Toggle(page, 0, 4, "System.ObsToggleStudioMode",
            new StarterState("Off", "Studio mode", "movie-open", Idle),
            new StarterState("On", "Studio mode", "movie-open", Enabled), ownedByCommand: true);

        // Example scene names; the setup note asks to rename them to the real scenes.
        for (int i = 0; i < 4; i++)
            b.Key(page, 1, i, $"System.ObsSetScene(Scene {i + 1})", $"Scene {i + 1}", $"numeric-{i + 1}-box");
        b.Key(page, 1, 4, "System.ObsTriggerTransition", "Transition", "swap-horizontal");

        // "Mic/Aux" and "Desktop Audio" are the inputs a fresh OBS installation creates.
        b.Key(page, 2, 0, "System.ObsToggleInputMute(Mic/Aux)", "Mic", "microphone-off");
        b.Key(page, 2, 1, "System.ObsToggleInputMute(Desktop Audio)", "Desktop", "volume-off");
        b.Toggle(page, 2, 2, "System.ObsToggleReplay",
            new StarterState("Off", "Replay", "replay", Idle),
            new StarterState("On", "Replay", "replay", Enabled), ownedByCommand: true);
        b.Key(page, 2, 3, "System.ObsSaveReplay", "Save replay", "content-save");

        if (!b.HasDials)
            b.Key(page, 2, 4, Mute, "Mute", "volume-off");

        b.AddDialPage(workspace, "Control", VolumeDial);
    }
}