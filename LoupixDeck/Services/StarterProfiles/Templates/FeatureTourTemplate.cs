using Avalonia.Media;
using LoupixDeck.Models;
using LoupixDeck.Services.Folders;
using static LoupixDeck.Services.StarterProfiles.StarterCommands;

namespace LoupixDeck.Services.StarterProfiles.Templates;

/// <summary>
/// One example of each building block: workspaces, pages, nested folders, a key with states, a
/// command sequence, a typed macro, a live clock and dials with a second dial page. Needs no plugin.
/// </summary>
internal sealed class FeatureTourTemplate : StarterProfileTemplate
{
    private static readonly Color Unmuted = Color.Parse("#1F6E43");
    private static readonly Color Muted = Color.Parse("#B3261E");

    public override string Id => "feature-tour";
    protected override string NameKey => "StarterProfile_Tour_Name";
    protected override string DescriptionKey => "StarterProfile_Tour_Description";
    public override string SymbolId => "compass";

    public override void Build(StarterProfileBuilder b, Profile profile)
    {
        Workspace tour = b.AddWorkspace(profile, "Tour");
        Workspace tools = b.AddWorkspace(profile, "Tools");

        BuildTour(b, tour, tools);
        BuildTools(b, tools);
    }

    private static void BuildTour(StarterProfileBuilder b, Workspace tour, Workspace tools)
    {
        TouchButtonPage first = b.AddTouchPage(tour, "First page");
        TouchButtonPage second = b.AddTouchPage(tour, "Second page");

        CustomFolder links = b.AddFolder(tour, "Links");
        CustomFolder more = b.AddFolder(tour, "More", links);

        b.Key(first, 0, 0, FolderCommand.Build(links.Id), "Folder", "folder");
        b.Toggle(first, 0, 1, Mute,
            new StarterState("Sound on", "Sound on", "volume-high", Unmuted),
            new StarterState("Muted", "Muted", "volume-off", Muted), ownedByCommand: false);
        b.Key(first, 0, 2, $"{Keys("Ctrl+A")} && {Keys("Ctrl+C")}", "Copy all", "select-all");
        b.Key(first, 0, 3, "System.SimpleMacro(Hello from LoupixDeck)", "Type text", "keyboard");
        b.Display(first, 0, 4, Clock);

        b.Key(first, 1, 0, "System.NextPage", "Next page", "arrow-right");
        b.Key(first, 1, 1, Workspace(tools.Id), "Tools", "swap-horizontal");
        b.Key(first, 1, 2, Url(ProjectUrl), "Website", "web");

        if (!b.HasDials)
        {
            b.Key(first, 2, 0, VolumeDown, "Volume -", "volume-minus");
            b.Key(first, 2, 1, VolumeUp, "Volume +", "volume-plus");
        }

        b.Key(second, 0, 0, "System.PreviousPage", "Back", "arrow-left");
        b.Key(second, 0, 1, PlayPause, "Play/Pause", "play-pause");
        b.Key(second, 0, 2, PreviousTrack, "Previous", "skip-previous");
        b.Key(second, 0, 3, NextTrack, "Next", "skip-next");

        b.Key(links.Layout, 0, 0, Url(ProjectUrl), "Project", "source-repository");
        b.Key(links.Layout, 0, 1, Url(ProjectUrl + "/issues"), "Issues", "bug");
        b.Key(links.Layout, 0, 2, FolderCommand.Build(more.Id), "More", "folder-multiple");

        b.Display(more.Layout, 0, 0, Clock);
        b.Key(more.Layout, 0, 1, FolderCommand.CloseName, "Close all", "close");

        // A dial press switches between the two dial pages.
        b.AddDialPage(tour, "Dials 1",
            VolumeDial,
            new StarterDial("Pages", "System.PreviousPage", "System.NextPage", "System.NextRotaryPage"),
            new StarterDial("Brightness", "System.BrightnessDown", "System.BrightnessUp"),
            new StarterDial("Workspace", "System.PreviousWorkspace", "System.NextWorkspace", "System.GoHomeWorkspace"),
            new StarterDial("Scroll", "System.MouseScroll(-1)", "System.MouseScroll(1)"),
            TrackDial);
        b.AddDialPage(tour, "Dials 2",
            TrackDial,
            new StarterDial("Pages", "System.PreviousPage", "System.NextPage", "System.PreviousRotaryPage"),
            new StarterDial("Zoom", Keys("Ctrl+NumMinus"), Keys("Ctrl+NumPlus")),
            new StarterDial("Undo", Keys("Ctrl+Z"), Redo(b.IsWindows)),
            new StarterDial("Tab", Keys("Ctrl+Shift+Tab"), Keys("Ctrl+Tab")),
            VolumeDial);
    }

    private static void BuildTools(StarterProfileBuilder b, Workspace tools)
    {
        TouchButtonPage page = b.AddTouchPage(tools, "Tools");

        b.Key(page, 0, 0, "System.GoHomeWorkspace", "Tour", "home");
        b.Key(page, 0, 1, Keys("Ctrl+C"), "Copy", "content-copy");
        b.Key(page, 0, 2, Keys("Ctrl+V"), "Paste", "content-paste");
        b.Key(page, 0, 3, Keys("Ctrl+Z"), "Undo", "undo");
        b.Key(page, 0, 4, Keys("Ctrl+X"), "Cut", "content-cut");

        b.AddDialPage(tools, "Tools", VolumeDial);
    }
}