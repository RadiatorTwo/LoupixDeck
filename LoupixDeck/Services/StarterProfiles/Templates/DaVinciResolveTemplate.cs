using Avalonia.Media;
using LoupixDeck.Models;
using static LoupixDeck.Services.StarterProfiles.StarterCommands;

namespace LoupixDeck.Services.StarterProfiles.Templates;

/// <summary>
/// Editing in DaVinci Resolve through its default keyboard shortcuts, which are the same on
/// Windows and Linux. Needs no plugin.
/// </summary>
internal sealed class DaVinciResolveTemplate : StarterProfileTemplate
{
    public override string Id => "davinci-resolve";
    protected override string NameKey => "StarterProfile_Resolve_Name";
    protected override string DescriptionKey => "StarterProfile_Resolve_Description";
    protected override string SetupNoteKey => "StarterProfile_Resolve_SetupNote";
    public override string SymbolId => "movie-roll";

    public override StarterIconStyle IconStyle { get; } =
        new(Color.Parse("#8BE6EE"), Color.Parse("#FFB36B"));

    public override void Build(StarterProfileBuilder b, Profile profile)
    {
        Workspace workspace = b.AddWorkspace(profile, "Edit");
        TouchButtonPage page = b.AddTouchPage(workspace, "Edit");

        b.Key(page, 0, 0, Keys("Space"), "Play/Stop", "play-pause");
        b.Key(page, 0, 1, Keys("J"), "Reverse", "rewind");
        b.Key(page, 0, 2, Keys("K"), "Stop", "stop");
        b.Key(page, 0, 3, Keys("L"), "Forward", "fast-forward");
        b.Key(page, 0, 4, Keys("X"), "Mark clip", "select");

        b.Key(page, 1, 0, Keys("I"), "Mark in", "ray-start");
        b.Key(page, 1, 1, Keys("O"), "Mark out", "ray-end");
        b.Key(page, 1, 2, Keys("A"), "Select", "cursor-default");
        b.Key(page, 1, 3, Keys("B"), "Blade", "content-cut");
        b.Key(page, 1, 4, Keys("T"), "Trim", "arrow-expand-horizontal");

        b.Key(page, 2, 0, Keys("Ctrl+Z"), "Undo", "undo");
        b.Key(page, 2, 1, Keys("Ctrl+Shift+Z"), "Redo", "redo");
        b.Key(page, 2, 2, Keys("M"), "Marker", "map-marker");
        b.Key(page, 2, 3, Keys("N"), "Snapping", "magnet");
        b.Key(page, 2, 4, Keys("Ctrl+S"), "Save", "content-save");

        b.AddDialPage(workspace, "Edit",
            new StarterDial("Frame", Keys("Left"), Keys("Right"), Keys("Space")),
            new StarterDial("Zoom", Keys("Ctrl+Minus"), Keys("Ctrl+Equals")),
            new StarterDial("Edit point", Keys("Up"), Keys("Down")),
            new StarterDial("Second", Keys("Shift+Left"), Keys("Shift+Right")),
            VolumeDial);
    }
}