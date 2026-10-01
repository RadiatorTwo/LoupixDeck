using Avalonia.Media;
using LoupixDeck.Models;
using static LoupixDeck.Services.StarterProfiles.StarterCommands;

namespace LoupixDeck.Services.StarterProfiles.Templates;

/// <summary>Everyday keys that work without any plugin: media, clipboard, a clock and the volume.</summary>
internal sealed class BasicStarterTemplate : StarterProfileTemplate
{
    public override string Id => "starter";
    protected override string NameKey => "StarterProfile_Basic_Name";
    protected override string DescriptionKey => "StarterProfile_Basic_Description";
    public override string SymbolId => "rocket-launch";

    public override StarterIconStyle IconStyle { get; } =
        new(Color.Parse("#A9C9FF"), Color.Parse("#B98BFF"));

    public override void Build(StarterProfileBuilder b, Profile profile)
    {
        Workspace workspace = b.AddWorkspace(profile, "Home");
        TouchButtonPage page = b.AddTouchPage(workspace, "Main");

        b.Key(page, 0, 0, PlayPause, "Play/Pause", "play-pause");
        b.Key(page, 0, 1, PreviousTrack, "Previous", "skip-previous");
        b.Key(page, 0, 2, NextTrack, "Next", "skip-next");
        b.Key(page, 0, 3, Mute, "Mute", "volume-off");
        b.Display(page, 0, 4, Clock);

        b.Key(page, 1, 0, Keys("Ctrl+C"), "Copy", "content-copy");
        b.Key(page, 1, 1, Keys("Ctrl+V"), "Paste", "content-paste");
        b.Key(page, 1, 2, Keys("Ctrl+Z"), "Undo", "undo");
        b.Key(page, 1, 3, b.IsWindows ? Keys("Win+Shift+S") : Keys("PrintScreen"), "Screenshot", "monitor-screenshot");
        b.Key(page, 1, 4, Redo(b.IsWindows), "Redo", "redo");

        b.Key(page, 2, 0, Url(ProjectUrl), "LoupixDeck", "web");
        b.Key(page, 2, 1, "System.BrightnessDown", "Dimmer", "brightness-5");
        b.Key(page, 2, 2, "System.BrightnessUp", "Brighter", "brightness-7");

        if (b.HasDials)
        {
            b.Key(page, 2, 3, Keys("Win+L"), "Lock", "lock");
            b.Key(page, 2, 4, b.IsWindows ? Keys("Ctrl+Shift+Esc") : Keys("Ctrl+Alt+T"),
                b.IsWindows ? "Task Manager" : "Terminal", b.IsWindows ? "monitor-dashboard" : "console");
        }
        else
        {
            // Without dials the volume needs keys of its own.
            b.Key(page, 2, 3, VolumeDown, "Volume -", "volume-minus");
            b.Key(page, 2, 4, VolumeUp, "Volume +", "volume-plus");
        }

        b.AddDialPage(workspace, "Main",
            VolumeDial,
            new StarterDial("Scroll", "System.MouseScroll(-1)", "System.MouseScroll(1)"),
            new StarterDial("Brightness", "System.BrightnessDown", "System.BrightnessUp"),
            TrackDial,
            new StarterDial("Undo", Keys("Ctrl+Z"), Redo(b.IsWindows)),
            new StarterDial("Zoom", Keys("Ctrl+NumMinus"), Keys("Ctrl+NumPlus")));
    }
}