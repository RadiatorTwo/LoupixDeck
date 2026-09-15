using LoupixDeck.Models;

namespace LoupixDeck.Services.Import.Lp5;

/// <summary>Target device shape an import is planned against.</summary>
public sealed record Lp5DeviceShape(int TouchButtonCount, int RotaryButtonCount, int SideRotaryButtonCount, bool HasIndependentRotarySides);

public sealed record Lp5PlannedKey(string Action, Lp5PressMapping Mapping);

public sealed record Lp5PlannedTouchPage(string Name, IReadOnlyList<Lp5PlannedKey> Keys);

public sealed record Lp5PlannedDial(string PressAction, Lp5PressMapping Press, Lp5RotateMapping Rotate);

public sealed record Lp5PlannedRotaryPage(string Name, RotarySide Side, IReadOnlyList<Lp5PlannedDial> Dials);

public sealed record Lp5PlannedWorkspace(string Name, bool IsHome, IReadOnlyList<Lp5PlannedTouchPage> TouchPages,
    IReadOnlyList<Lp5PlannedRotaryPage> RotaryPages);

/// <summary>An action that is not carried over, with where it sits.</summary>
public sealed record Lp5UnmappedControl(string Location, string Label, Lp5UnmappedReason Reason, string Detail);

public enum Lp5NoteKind
{
    /// <summary>A page had more keys or dials than this device; <c>Detail</c> is the page name.</summary>
    PageSplit,

    /// <summary><c>Count</c> controls carry an Fn-layer action, which is not imported.</summary>
    FnActionsSkipped,

    /// <summary>This device has no dials; <c>Count</c> dial pages are not imported.</summary>
    NoDials,

    /// <summary>The file contains no pages at all.</summary>
    Empty
}

public sealed record Lp5Note(Lp5NoteKind Kind, string Detail = null, int Count = 0);

/// <summary>
/// Everything a Loupedeck import will do, computed without touching the config, the macro store or the
/// asset store — the preview dialog shows it, and <see cref="Lp5ProfileBuilder"/> carries it out.
/// </summary>
public sealed class Lp5ImportPlan
{
    public required Lp5Package Package { get; init; }
    public required IReadOnlyList<Lp5PlannedWorkspace> Workspaces { get; init; }
    public required IReadOnlyList<Lp5MacroDraft> Macros { get; init; }
    public required IReadOnlyList<Lp5UnmappedControl> Unmapped { get; init; }
    public required IReadOnlyList<Lp5Note> Notes { get; init; }
    public int TotalControls { get; init; }
    public int MappedControls { get; init; }

    /// <summary>Plans importing <paramref name="package"/> onto a device of <paramref name="shape"/>.</summary>
    public static Lp5ImportPlan Create(Lp5Package package, Lp5DeviceShape shape, Func<string, bool> isMacroNameTaken)
    {
        Lp5ActionMapper mapper = new(package, isMacroNameTaken);
        List<Lp5PlannedWorkspace> workspaces = [];
        List<Lp5UnmappedControl> unmapped = [];
        List<Lp5Note> notes = [];
        int total = 0;
        int mapped = 0;
        int skippedDialPages = 0;

        for (int w = 0; w < package.Workspaces.Count; w++)
        {
            Lp5Workspace source = package.Workspaces[w];
            string workspaceName = string.IsNullOrWhiteSpace(source.Name) ? package.Name : source.Name;
            bool single = package.Workspaces.Count == 1;

            List<Lp5PlannedTouchPage> touchPages = [];
            foreach (Lp5TouchPage page in source.TouchPages)
            {
                List<Lp5PlannedKey> keys = [];
                for (int i = 0; i < page.PressActions.Count; i++)
                {
                    string action = page.PressActions[i];
                    Lp5PressMapping mapping = mapper.MapPress(action);
                    keys.Add(mapping == null ? null : new Lp5PlannedKey(action, mapping));
                    if (mapping == null) continue;

                    total++;
                    if (mapping.IsMapped)
                        mapped++;
                    else
                        unmapped.Add(new Lp5UnmappedControl(Location(single, workspaceName, page.Name, i), mapping.Label,
                            mapping.Reason ?? Lp5UnmappedReason.UnsupportedAction, mapping.Detail));
                }

                AddChunked(touchPages, page.Name, keys, Math.Max(1, shape.TouchButtonCount), notes,
                    (name, chunk) => new Lp5PlannedTouchPage(name, chunk));
            }

            List<Lp5PlannedRotaryPage> rotaryPages = [];
            foreach (Lp5EncoderPage page in source.EncoderPages)
            {
                List<Lp5PlannedDial> dials = [];
                for (int i = 0; i < page.Encoders.Count; i++)
                {
                    Lp5Encoder encoder = page.Encoders[i];
                    Lp5PressMapping press = mapper.MapPress(encoder.PressAction);
                    Lp5RotateMapping rotate = mapper.MapRotate(encoder.RotateAction);
                    dials.Add(press == null && rotate == null ? null : new Lp5PlannedDial(encoder.PressAction, press, rotate));

                    string location = Location(single, workspaceName, page.Name, i);
                    if (press != null)
                    {
                        total++;
                        if (press.IsMapped) mapped++;
                        else unmapped.Add(new Lp5UnmappedControl(location, press.Label, press.Reason ?? Lp5UnmappedReason.UnsupportedAction, press.Detail));
                    }

                    if (rotate != null)
                    {
                        total++;
                        if (rotate.IsMapped) mapped++;
                        else unmapped.Add(new Lp5UnmappedControl(location, rotate.Label, rotate.Reason ?? Lp5UnmappedReason.UnsupportedAction, rotate.Detail));
                    }
                }

                if (dials.All(d => d == null))
                    continue;

                if (shape.RotaryButtonCount <= 0 && !shape.HasIndependentRotarySides)
                {
                    skippedDialPages++;
                    continue;
                }

                if (shape.HasIndependentRotarySides)
                {
                    // Loupedeck lists the left column first, then the right one.
                    int half = (dials.Count + 1) / 2;
                    int side = Math.Max(1, shape.SideRotaryButtonCount);
                    AddChunked(rotaryPages, page.Name, dials.Take(half).ToList(), side, notes,
                        (name, chunk) => new Lp5PlannedRotaryPage(name, RotarySide.Left, chunk));
                    AddChunked(rotaryPages, page.Name, dials.Skip(half).ToList(), side, notes,
                        (name, chunk) => new Lp5PlannedRotaryPage(name, RotarySide.Right, chunk));
                }
                else
                {
                    AddChunked(rotaryPages, page.Name, dials, shape.RotaryButtonCount, notes,
                        (name, chunk) => new Lp5PlannedRotaryPage(name, RotarySide.Both, chunk));
                }
            }

            bool isHome = source.Key.Length > 0 && string.Equals(source.Key, package.HomeWorkspaceKey, StringComparison.OrdinalIgnoreCase);
            workspaces.Add(new Lp5PlannedWorkspace(workspaceName, isHome, touchPages, rotaryPages));
        }

        if (package.FnActionCount > 0)
            notes.Add(new Lp5Note(Lp5NoteKind.FnActionsSkipped, Count: package.FnActionCount));
        if (skippedDialPages > 0)
            notes.Add(new Lp5Note(Lp5NoteKind.NoDials, Count: skippedDialPages));
        if (workspaces.All(ws => ws.TouchPages.Count == 0 && ws.RotaryPages.Count == 0))
            notes.Add(new Lp5Note(Lp5NoteKind.Empty));

        return new Lp5ImportPlan
        {
            Package = package,
            Workspaces = workspaces,
            Macros = mapper.Macros,
            Unmapped = unmapped,
            Notes = notes,
            TotalControls = total,
            MappedControls = mapped
        };
    }

    /// <summary>
    /// Splits <paramref name="items"/> into pages of <paramref name="size"/>, dropping trailing empty
    /// slots first so a page that merely has more (empty) slots than this device does not spawn pages.
    /// </summary>
    private static void AddChunked<TItem, TPage>(List<TPage> pages, string name, List<TItem> items, int size,
        List<Lp5Note> notes, Func<string, IReadOnlyList<TItem>, TPage> create) where TItem : class
    {
        int count = items.Count;
        while (count > 0 && items[count - 1] == null)
            count--;

        if (count == 0)
        {
            pages.Add(create(name, []));
            return;
        }

        int chunks = (count + size - 1) / size;
        for (int c = 0; c < chunks; c++)
        {
            string pageName = c == 0 ? name : $"{name} ({c + 1})";
            pages.Add(create(pageName, items.Skip(c * size).Take(Math.Min(size, count - (c * size))).ToList()));
        }

        if (chunks > 1)
            notes.Add(new Lp5Note(Lp5NoteKind.PageSplit, name));
    }

    private static string Location(bool singleWorkspace, string workspace, string page, int index) =>
        singleWorkspace ? $"{page} · {index + 1}" : $"{workspace} › {page} · {index + 1}";
}
