using Avalonia.Media;
using LoupixDeck.Localization;
using LoupixDeck.LoupedeckDevice;
using LoupixDeck.LoupedeckDevice.Device;
using LoupixDeck.Models;
using LoupixDeck.Models.Layers;
using Newtonsoft.Json.Linq;

namespace LoupixDeck.Services.Import.Lp5;

/// <summary>
/// Converts a Loupedeck <c>.lp5</c> profile into a LoupixDeck <see cref="Profile"/> for the attached
/// device: workspaces, touch pages, dial pages (split into left and right columns where the device
/// has them, with the dial icons drawn on the side strips), and the round LED buttons.
/// </summary>
/// <remarks>
/// <para>Ported from <c>lp5_to_loupix.py</c> of loupedeck-to-loupixdeck by Vencite (MIT license,
/// https://github.com/Vencite/loupedeck-to-loupixdeck, issue #289).</para>
/// <para>The profile is built detached: attaching it to the config is the caller's job, and must be
/// followed by a config save in the same UI continuation, or the asset cleanup of another device's
/// save could sweep the freshly stored icons. Pass no asset store for a preview pass that writes
/// nothing.</para>
/// </remarks>
public sealed class Lp5Converter
{
    /// <summary>Loupedeck sizes its layouts for a 90 px key; offsets below are in those pixels.</summary>
    private const double ReferenceKeySize = 90.0;

    /// <summary>Round LED buttons on a Loupedeck Live / Razer Stream Controller.</summary>
    private const int RoundButtonCount = 8;

    private const int MaxDialLabelLength = 18;

    private static readonly Color UnmappedBackground = Color.Parse("#54202B");

    private readonly Lp5Archive _archive;
    private readonly Lp5DeviceShape _shape;
    private readonly Lp5LayerFactory _layers;
    private readonly Lp5ActionResolver _resolver;
    private readonly Dictionary<string, Guid> _workspaceIds = new(StringComparer.Ordinal);
    private readonly List<Lp5UnsupportedControl> _unsupported = [];
    private readonly List<Lp5MovedControl> _moved = [];
    private int _totalControls;
    private int _mappedControls;
    private int _movedKeys;
    private int _movedDials;
    private int _wheelPages;

    private Lp5Converter(Lp5Archive archive, Lp5DeviceShape shape, IAssetService assets)
    {
        _archive = archive;
        _shape = shape;
        _layers = new Lp5LayerFactory(archive, assets, shape.Geometry.KeySize);

        foreach (JToken workspace in Lp5Json.Arr(archive.LayoutMode, "workspaces"))
        {
            if (Lp5Json.Str(workspace, "name") is { Length: > 0 } name)
                _workspaceIds.TryAdd(name, Guid.NewGuid());
        }

        _resolver = new Lp5ActionResolver(archive, _workspaceIds, shape.HasIndependentRotarySides);
    }

    /// <summary>Scale from Loupedeck's 90 px reference key to this device's key.</summary>
    private double KeyScale => _shape.Geometry.KeySize / ReferenceKeySize;

    /// <summary>Converts <paramref name="archive"/> for a device of <paramref name="shape"/>.</summary>
    /// <param name="assets">Asset store for icons; null for a preview that stores nothing.</param>
    public static Lp5ConversionResult Convert(Lp5Archive archive, Lp5DeviceShape shape, IAssetService assets) =>
        new Lp5Converter(archive, shape, assets).Run();

    /// <summary>
    /// False for a wheel page that still holds Loupedeck's default (analog clock, press shows the clock,
    /// turn scrolls): every workspace gets one, and skipping it loses nothing the user set up.
    /// </summary>
    private bool IsCustomWheelPage(string name)
    {
        JToken page = Lp5Json.Arr(_archive.LayoutMode, "wheelPages").FirstOrDefault(p => Lp5Json.Str(p, "name") == name);
        if (page == null) return false;

        JObject parameters = Lp5Json.Obj(page, "parameters");
        return Lp5Json.Str(page, "templateName") != "WheelToolAnalogClock"
               || Lp5Json.Str(parameters, "actions") != "$@Generic___@ButtonClock"
               || Lp5Json.Str(parameters, "adjustment") != "$@Generic___@MouseWheel";
    }

    private Lp5ConversionResult Run()
    {
        Profile profile = new() { Name = _archive.ProfileName };
        int touchPages = 0;
        int rotaryPages = 0;

        foreach (JToken source in Lp5Json.Arr(_archive.LayoutMode, "workspaces"))
        {
            string sourceId = Lp5Json.Str(source, "name");
            Workspace workspace = ConvertWorkspace(source, sourceId);
            profile.Workspaces.Add(workspace);

            touchPages += workspace.TouchButtonPages.Count;
            rotaryPages += workspace.RotaryButtonPages.Count + workspace.LeftRotaryButtonPages.Count;
            _wheelPages += Lp5Json.Strings(source, "wheelPageNames").Count(IsCustomWheelPage);
        }

        if (profile.Workspaces.Count == 0)
            profile.Workspaces.Add(new Workspace { Name = Loc.Tr("LoupedeckImport_FallbackWorkspace") });

        string homeSourceId = FindHomeWorkspace(profile);
        profile.HomeWorkspaceId = homeSourceId != null && _workspaceIds.TryGetValue(homeSourceId, out Guid homeId)
            ? homeId
            : profile.Workspaces[0].Id;

        if (_shape.Geometry.HasLedButtons)
            profile.SimpleButtons = ConvertRoundButtons(profile.HomeWorkspaceId);

        List<Lp5Note> notes = [];
        AddNote(notes, Lp5NoteKind.WheelPagesSkipped, _wheelPages);
        AddNote(notes, Lp5NoteKind.MovedKeys, _movedKeys);
        AddNote(notes, Lp5NoteKind.MovedDials, _movedDials);
        AddNote(notes, Lp5NoteKind.UnreadableIcons, _layers.UnreadableIcons);

        return new Lp5ConversionResult
        {
            Profile = profile,
            Unsupported = _unsupported,
            Moved = _moved,
            Notes = notes,
            Workspaces = profile.Workspaces.Count,
            TouchPages = touchPages,
            RotaryPages = rotaryPages,
            TotalControls = _totalControls,
            MappedControls = _mappedControls,
            Icons = _layers.Icons
        };
    }

    private static void AddNote(List<Lp5Note> notes, Lp5NoteKind kind, int count)
    {
        if (count > 0)
            notes.Add(new Lp5Note(kind, count));
    }

    private Workspace ConvertWorkspace(JToken source, string sourceId)
    {
        string name = Lp5Json.Str(source, "displayName") is { Length: > 0 } displayName ? displayName : Loc.Tr("LoupedeckImport_FallbackWorkspace");
        Workspace workspace = new() { Name = name };
        if (sourceId != null && _workspaceIds.TryGetValue(sourceId, out Guid id))
            workspace.Id = id;

        List<string> touchNames = Lp5Json.Strings(source, "touchPageNames").ToList();
        List<string> encoderNames = Lp5Json.Strings(source, "encoderPageNames").ToList();
        Lp5Context context = new(sourceId, touchNames, encoderNames);

        // Controls that do not fit go to extra pages. Those are appended after all source pages, so the
        // page switches, which address pages by their source position, still land on the right page.
        List<TouchButtonPage> extraTouchPages = [];
        List<(RotaryButtonPage Left, RotaryButtonPage Right)> extraSplitPages = [];
        List<RotaryButtonPage> extraSharedPages = [];

        Dictionary<string, JObject> touchPages = PagesById("touchPages");
        foreach (string pageId in touchNames)
        {
            if (pageId != null && touchPages.TryGetValue(pageId, out JObject page))
            {
                workspace.TouchButtonPages.Add(ConvertTouchPage(page, workspace.TouchButtonPages.Count + 1, name, context,
                    extraTouchPages));
            }
        }

        Dictionary<string, JObject> encoderPages = PagesById("encoderPages");
        foreach (string pageId in encoderNames)
        {
            if (pageId == null || !encoderPages.TryGetValue(pageId, out JObject page)) continue;

            if (_shape.HasIndependentRotarySides)
            {
                int number = workspace.LeftRotaryButtonPages.Count + 1;
                (RotaryButtonPage left, RotaryButtonPage right) = ConvertSplitDialPage(page, number, name, context,
                    extraSplitPages);
                workspace.LeftRotaryButtonPages.Add(left);
                workspace.RightRotaryButtonPages.Add(right);
            }
            else
            {
                workspace.RotaryButtonPages.Add(ConvertSharedDialPage(page, workspace.RotaryButtonPages.Count + 1, name,
                    context, extraSharedPages));
            }
        }

        foreach (TouchButtonPage page in extraTouchPages)
            workspace.TouchButtonPages.Add(page);
        foreach ((RotaryButtonPage left, RotaryButtonPage right) in extraSplitPages)
        {
            workspace.LeftRotaryButtonPages.Add(left);
            workspace.RightRotaryButtonPages.Add(right);
        }
        foreach (RotaryButtonPage page in extraSharedPages)
            workspace.RotaryButtonPages.Add(page);

        return workspace;
    }

    private Dictionary<string, JObject> PagesById(string key)
    {
        Dictionary<string, JObject> pages = new(StringComparer.Ordinal);
        foreach (JObject page in Lp5Json.Arr(_archive.LayoutMode, key).OfType<JObject>())
        {
            if (Lp5Json.Str(page, "name") is { Length: > 0 } id)
                pages.TryAdd(id, page);
        }

        return pages;
    }

    // ---- Touch pages -------------------------------------------------------------------------

    private TouchButtonPage ConvertTouchPage(JObject source, int number, string workspaceName, Lp5Context context,
        List<TouchButtonPage> extraPages)
    {
        string pageName = Lp5Json.Str(source, "displayName") ?? string.Empty;
        string shownName = pageName.Length > 0 ? pageName : Loc.Tr("LoupedeckImport_LocationPage", number);
        TouchButtonPage page = new(_shape.TouchButtonCount) { Name = pageName };
        IReadOnlyList<JToken> controls = Lp5Json.Arr(source, "controls");
        string location = $"{workspaceName} › {shownName}";
        List<(int Index, string ActionRef)> surplus = [];
        context = context with { PageId = Lp5Json.Str(source, "name") };

        for (int i = 0; i < controls.Count; i++)
        {
            string actionRef = Lp5Json.Str(controls[i], "pressAction");
            if (Lp5ActionResolver.IsNone(actionRef)) continue;

            int slot = TouchSlot(i, controls.Count);
            if (slot < 0)
            {
                surplus.Add((i, actionRef));
                continue;
            }

            ConvertKey(page.TouchButtons[slot], actionRef, context, $"{location} › {Loc.Tr("LoupedeckImport_LocationKey", slot + 1)}");
        }

        // Keys the grid has no room for fill extra pages in their source order.
        int perPage = Math.Min(_shape.Geometry.GridSlots, _shape.TouchButtonCount);
        for (int start = 0; perPage > 0 && start < surplus.Count; start += perPage)
        {
            string extraName = ExtraPageName(shownName, start / perPage);
            string extraLocation = $"{workspaceName} › {extraName}";
            TouchButtonPage extra = new(_shape.TouchButtonCount) { Name = extraName };

            for (int slot = 0; slot < perPage && start + slot < surplus.Count; slot++)
            {
                (int index, string actionRef) = surplus[start + slot];
                string to = $"{extraLocation} › {Loc.Tr("LoupedeckImport_LocationKey", slot + 1)}";
                ConvertKey(extra.TouchButtons[slot], actionRef, context, to);
                _moved.Add(new Lp5MovedControl(_resolver.Label(actionRef),
                    $"{location} › {Loc.Tr("LoupedeckImport_LocationKey", index + 1)}", to));
                _movedKeys++;
            }

            extraPages.Add(extra);
        }

        return page;
    }

    /// <summary>The name of the <paramref name="extra"/>-th (0-based) extra page of a page: "Main (2)", "Main (3)".</summary>
    private static string ExtraPageName(string pageName, int extra) => $"{pageName} ({extra + 2})";

    /// <summary>
    /// The target slot for source key <paramref name="index"/>, or -1 when the device has no room for
    /// it. Keys keep their row and column, so a 4×3 Loupedeck Live page stays a 4×3 block on a wider
    /// grid instead of wrapping.
    /// </summary>
    private int TouchSlot(int index, int sourceCount)
    {
        int columns = _shape.Geometry.Columns;
        int rows = _shape.Geometry.Rows;
        int gridSlots = Math.Min(_shape.Geometry.GridSlots, _shape.TouchButtonCount);

        // Loupedeck Live / CT pages hold 4×3 keys, Live S pages 5×3.
        int sourceColumns = sourceCount switch
        {
            12 => 4,
            15 => 5,
            _ => columns
        };

        if (sourceColumns == columns)
            return index < gridSlots ? index : -1;

        int row = index / sourceColumns;
        int column = index % sourceColumns;
        int slot = (row * columns) + column;
        return column < columns && row < rows && slot < gridSlots ? slot : -1;
    }

    private void ConvertKey(TouchButton button, string actionRef, Lp5Context context, string location)
    {
        _totalControls++;
        Lp5Resolution resolution = _resolver.Resolve(actionRef, context);
        string label = _resolver.Label(actionRef);

        if (resolution.Command == null)
        {
            Record(location, label, resolution);

            // Keep the key visible so the gap is obvious on the device. A key with its own icon keeps it,
            // so recreating the action by hand leaves the look intact; a marker on top flags the gap.
            List<LayerBase> icon = _layers.IconOrImageLayers(actionRef, label);
            TextLayer caption;
            if (icon.Count > 0)
            {
                FitCaptionUnderIcon(icon);
                foreach (LayerBase layer in icon)
                    button.Layers.Add(layer);

                caption = Lp5LayerFactory.Caption(Loc.Tr("LoupedeckImport_KeyNotImported"), Loc.Tr("LoupedeckImport_KeyNotImported"));
                caption.BoxHeight = (int)Math.Round(18 * KeyScale);
                caption.PositionY = -(int)Math.Round(34 * KeyScale);
            }
            else
            {
                string shortLabel = label.Length > 12 ? label[..11] + "…" : label;
                caption = Lp5LayerFactory.Caption(Loc.Tr("LoupedeckImport_KeyNotImported") + "\n" + shortLabel,
                    Loc.Tr("LoupedeckImport_KeyNotImported"));
                caption.BoxHeight = (int)Math.Round(84 * KeyScale);
            }

            caption.TextSize = 10;
            caption.BoxWidth = (int)Math.Round(84 * KeyScale);
            button.Layers.Add(caption);
            button.BackColor = UnmappedBackground;
            button.BackgroundEnabled = true;
            button.RewireLayerHandlers();
            return;
        }

        _mappedControls++;
        button.Command = resolution.Command;

        List<LayerBase> layers = _layers.ButtonLayers(actionRef, label);
        FitCaptionUnderIcon(layers);
        foreach (LayerBase layer in layers)
            button.Layers.Add(layer);
        button.RewireLayerHandlers();
    }

    /// <summary>
    /// A Loupedeck caption at the bottom of an icon key is tiny on the device; make it readable and
    /// shrink the icon above it so the two do not overlap.
    /// </summary>
    private void FitCaptionUnderIcon(List<LayerBase> layers)
    {
        if (!layers.OfType<ImageLayer>().Any()) return;

        List<TextLayer> bottom = layers.OfType<TextLayer>().Where(t => t.PositionY >= 25 * KeyScale).ToList();
        if (bottom.Count == 0) return;

        foreach (TextLayer caption in bottom)
        {
            caption.TextSize = Math.Max(12, caption.TextSize);
            caption.BoxHeight = (int)Math.Round(20 * KeyScale);
            caption.PositionY = (int)Math.Round(27 * KeyScale);
        }

        foreach (ImageLayer image in layers.OfType<ImageLayer>())
        {
            image.Scale = Math.Min(image.Scale, 0.65);
            image.PositionY -= (int)Math.Round(10 * KeyScale);
        }
    }

    // ---- Dial pages --------------------------------------------------------------------------

    /// <summary>
    /// A Loupedeck dial page (three dials left, three right) as a left and a right page, each with
    /// its dial icons drawn onto the side strip.
    /// </summary>
    private (RotaryButtonPage Left, RotaryButtonPage Right) ConvertSplitDialPage(JObject source, int number,
        string workspaceName, Lp5Context context, List<(RotaryButtonPage Left, RotaryButtonPage Right)> extraPages)
    {
        string pageName = Lp5Json.Str(source, "displayName") ?? string.Empty;
        string shownName = pageName.Length > 0 ? pageName : Loc.Tr("LoupedeckImport_LocationDials", number);
        IReadOnlyList<JToken> controls = Lp5Json.Arr(source, "controls");
        int perPage = _shape.SideRotaryButtonCount * 2;
        string location = $"{workspaceName} › {shownName}";

        (RotaryButtonPage left, RotaryButtonPage right) = FillSplitDialPage(pageName, controls.Take(perPage).ToList(),
            context, i => $"{location} › {Loc.Tr("LoupedeckImport_LocationDial", i + 1)}");

        // Assigned dials the device has no room for fill extra pages in their source order.
        List<(int Index, JToken Control)> surplus = perPage > 0 ? SurplusDials(controls, perPage) : [];
        for (int start = 0; start < surplus.Count; start += perPage)
        {
            List<(int Index, JToken Control)> chunk = surplus.Skip(start).Take(perPage).ToList();
            string extraName = ExtraPageName(shownName, start / perPage);
            string extraLocation = $"{workspaceName} › {extraName}";

            extraPages.Add(FillSplitDialPage(extraName, chunk.Select(c => c.Control).ToList(), context,
                i => $"{extraLocation} › {Loc.Tr("LoupedeckImport_LocationDial", i + 1)}"));
            RecordMovedDials(chunk, location, extraLocation);
        }

        return (left, right);
    }

    /// <summary>Fills a left and a right page with up to one page's worth of dials, left column first.</summary>
    private (RotaryButtonPage Left, RotaryButtonPage Right) FillSplitDialPage(string pageName,
        IReadOnlyList<JToken> controls, Lp5Context context, Func<int, string> location)
    {
        int perSide = _shape.SideRotaryButtonCount;
        RotaryButtonPage left = new(perSide) { Name = pageName, Side = RotarySide.Left };
        RotaryButtonPage right = new(perSide) { Name = pageName, Side = RotarySide.Right };

        for (int i = 0; i < controls.Count; i++)
        {
            RotaryButtonPage page = i < perSide ? left : right;
            int index = i < perSide ? i : i - perSide;
            ConvertDial(page.RotaryButtons[index], controls[i], context, location(i));
        }

        DrawStrip(left, controls.Take(perSide).ToList(), StripCanvasIndex(RotarySide.Left));
        DrawStrip(right, controls.Skip(perSide).Take(perSide).ToList(), StripCanvasIndex(RotarySide.Right));
        return (left, right);
    }

    /// <summary>The assigned dials beyond the first <paramref name="fitting"/>, with their source position.</summary>
    private static List<(int Index, JToken Control)> SurplusDials(IReadOnlyList<JToken> controls, int fitting) =>
        controls.Select((control, index) => (index, control))
            .Skip(fitting)
            .Where(c => HasAssignment(c.control))
            .ToList();

    private void RecordMovedDials(List<(int Index, JToken Control)> chunk, string location, string extraLocation)
    {
        for (int i = 0; i < chunk.Count; i++)
        {
            JToken control = chunk[i].Control;
            string rotateRef = Lp5Json.Str(control, "rotateAction");
            string label = _resolver.Label(Lp5ActionResolver.IsNone(rotateRef) ? Lp5Json.Str(control, "pressAction") : rotateRef);
            _moved.Add(new Lp5MovedControl(label,
                $"{location} › {Loc.Tr("LoupedeckImport_LocationDial", chunk[i].Index + 1)}",
                $"{extraLocation} › {Loc.Tr("LoupedeckImport_LocationDial", i + 1)}"));
            _movedDials++;
        }
    }

    private static int StripCanvasIndex(RotarySide side) =>
        side == RotarySide.Left ? RazerStreamControllerDevice.LeftSideIndex : RazerStreamControllerDevice.RightSideIndex;

    private RotaryButtonPage ConvertSharedDialPage(JObject source, int number, string workspaceName, Lp5Context context,
        List<RotaryButtonPage> extraPages)
    {
        string pageName = Lp5Json.Str(source, "displayName") ?? string.Empty;
        string shownName = pageName.Length > 0 ? pageName : Loc.Tr("LoupedeckImport_LocationDials", number);
        IReadOnlyList<JToken> controls = Lp5Json.Arr(source, "controls");
        int count = _shape.RotaryButtonCount;
        string location = $"{workspaceName} › {shownName}";

        RotaryButtonPage page = new(count) { Name = pageName, Side = RotarySide.Both };
        for (int i = 0; i < Math.Min(count, controls.Count); i++)
            ConvertDial(page.RotaryButtons[i], controls[i], context, $"{location} › {Loc.Tr("LoupedeckImport_LocationDial", i + 1)}");

        // Assigned dials the device has no room for fill extra pages in their source order.
        List<(int Index, JToken Control)> surplus = count > 0 ? SurplusDials(controls, count) : [];
        for (int start = 0; start < surplus.Count; start += count)
        {
            List<(int Index, JToken Control)> chunk = surplus.Skip(start).Take(count).ToList();
            string extraName = ExtraPageName(shownName, start / count);
            string extraLocation = $"{workspaceName} › {extraName}";

            RotaryButtonPage extra = new(count) { Name = extraName, Side = RotarySide.Both };
            for (int i = 0; i < chunk.Count; i++)
            {
                ConvertDial(extra.RotaryButtons[i], chunk[i].Control, context,
                    $"{extraLocation} › {Loc.Tr("LoupedeckImport_LocationDial", i + 1)}");
            }

            extraPages.Add(extra);
            RecordMovedDials(chunk, location, extraLocation);
        }

        return page;
    }

    private static bool HasAssignment(JToken control) =>
        !Lp5ActionResolver.IsNone(Lp5Json.Str(control, "pressAction")) ||
        !Lp5ActionResolver.IsNone(Lp5Json.Str(control, "rotateAction"));

    private void ConvertDial(RotaryButton dial, JToken control, Lp5Context context, string location)
    {
        if (!HasAssignment(control)) return;

        string pressRef = Lp5Json.Str(control, "pressAction");
        string rotateRef = Lp5Json.Str(control, "rotateAction");
        Lp5DialResolution resolution = _resolver.ResolveDial(pressRef, rotateRef, context);

        _totalControls++;
        if (resolution.Press.Command != null || resolution.Left != null || resolution.Right != null)
            _mappedControls++;

        if (resolution.Press.Reason != null)
            Record(location, _resolver.Label(pressRef), resolution.Press);
        if (resolution.RotateReason is { } reason)
            _unsupported.Add(new Lp5UnsupportedControl(location, _resolver.Label(rotateRef), reason, resolution.RotateDetail));

        dial.Command = resolution.Press.Command;
        dial.RotaryLeftCommand = resolution.Left ?? string.Empty;
        dial.RotaryRightCommand = resolution.Right ?? string.Empty;
        dial.DisplayText = ShortenDialLabel(resolution.Label);
    }

    private static string ShortenDialLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return string.Empty;

        string collapsed = string.Join(' ', label.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= MaxDialLabelLength ? collapsed : collapsed[..(MaxDialLabelLength - 1)] + "…";
    }

    /// <summary>
    /// Draws the dial icons of one column onto its side strip, one per strip segment, and switches the
    /// page to free-draw mode. Pages without dial icons keep the segmented strip with the dial labels.
    /// </summary>
    private void DrawStrip(RotaryButtonPage page, IReadOnlyList<JToken> controls, int canvasIndex)
    {
        int stripWidth = _shape.Geometry.StripWidth;
        if (stripWidth <= 0) return;

        double segment = _shape.Geometry.PanelHeight / (double)RotaryButtonPage.StripSegmentCount;
        double segmentScale = segment / ReferenceKeySize;
        double toStrip = stripWidth / (double)_shape.Geometry.KeySize;

        List<LayerBase> stripLayers = [];
        for (int i = 0; i < controls.Count && i < RotaryButtonPage.StripSegmentCount; i++)
        {
            List<LayerBase> layers = _layers.ButtonLayers(Lp5Json.Str(controls[i], "rotateAction"), string.Empty);
            if (layers.Count == 0)
                layers = _layers.ButtonLayers(Lp5Json.Str(controls[i], "pressAction"), string.Empty);

            double offset = (i - ((RotaryButtonPage.StripSegmentCount - 1) / 2.0)) * segment;
            foreach (LayerBase layer in layers)
            {
                layer.PositionX = (int)Math.Round(layer.PositionX * toStrip);
                if (layer is TextLayer caption)
                {
                    caption.BoxWidth = stripWidth - 2;
                    caption.BoxHeight = (int)Math.Round(26 * segmentScale);
                    caption.TextSize = Math.Max(10, caption.TextSize);
                    caption.PositionY = (int)Math.Round(offset + (28 * segmentScale));
                }
                else
                {
                    layer.PositionY = (int)Math.Round((layer.PositionY * toStrip) + offset - (15 * segmentScale));
                }

                stripLayers.Add(layer);
            }
        }

        if (stripLayers.Count == 0) return;

        TouchButton canvas = new(canvasIndex);
        foreach (LayerBase layer in stripLayers)
            canvas.Layers.Add(layer);
        canvas.RewireLayerHandlers();

        page.StripCanvas = canvas;
        page.StripMode = StripMode.FreeDraw;
    }

    // ---- Round buttons and home workspace ----------------------------------------------------

    /// <summary>
    /// The source id of the home workspace: the explicit <c>homeWorkspaceName</c>, else one named
    /// Home/Main/Default, else the first.
    /// </summary>
    private string FindHomeWorkspace(Profile profile)
    {
        string explicitHome = Lp5Json.Str(_archive.LayoutMode, "homeWorkspaceName");
        if (explicitHome != null && _workspaceIds.ContainsKey(explicitHome))
            return explicitHome;

        Workspace named = profile.Workspaces.FirstOrDefault(w =>
            w.Name.Trim().ToLowerInvariant() is "home" or "main" or "default");
        Guid target = (named ?? profile.Workspaces[0]).Id;
        return _workspaceIds.FirstOrDefault(kv => kv.Value == target).Key;
    }

    /// <summary>
    /// The eight round LED buttons from <c>layout.roundPage</c>. Loupedeck treats an unassigned first
    /// button as the Home key, so it opens the home workspace here too.
    /// </summary>
    private SimpleButton[] ConvertRoundButtons(Guid homeWorkspaceId)
    {
        IReadOnlyList<JToken> controls = Lp5Json.Arr(Lp5Json.Obj(Lp5Json.Obj(_archive.Profile, "layout"), "roundPage"), "controls");
        SimpleButton[] buttons = new SimpleButton[RoundButtonCount];

        for (int i = 0; i < RoundButtonCount; i++)
        {
            string actionRef = i < controls.Count ? Lp5Json.Str(controls[i], "pressAction") : null;
            string command = null;

            if (!Lp5ActionResolver.IsNone(actionRef))
            {
                _totalControls++;
                Lp5Resolution resolution = _resolver.Resolve(actionRef, Lp5Context.Global);
                command = resolution.Command;
                if (command != null)
                    _mappedControls++;
                else
                    Record(Loc.Tr("LoupedeckImport_LocationRoundButton", i + 1), _resolver.Label(actionRef), resolution);
            }

            if (i == 0 && command == null)
                command = $"System.GotoWorkspace({homeWorkspaceId})";

            buttons[i] = new SimpleButton
            {
                Id = Constants.ButtonType.BUTTON0 + i,
                Command = command,
                ButtonColor = command != null ? Colors.Blue : Colors.Black
            };
        }

        return buttons;
    }

    private void Record(string location, string label, Lp5Resolution resolution) =>
        _unsupported.Add(new Lp5UnsupportedControl(location, label,
            resolution.Reason ?? Lp5UnsupportedReason.UnknownAction, resolution.Detail));
}
