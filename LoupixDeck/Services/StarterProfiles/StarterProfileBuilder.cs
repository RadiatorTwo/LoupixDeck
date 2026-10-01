using Avalonia.Media;
using LoupixDeck.LoupedeckDevice.Device;
using LoupixDeck.Models;
using LoupixDeck.Models.Layers;
using LoupixDeck.Registry;
using LoupixDeck.Services.Actions;
using LoupixDeck.Utils;

namespace LoupixDeck.Services.StarterProfiles;

/// <summary>One dial of a starter profile: what a turn left, a turn right and a press run.</summary>
public sealed record StarterDial(string Label, string Left, string Right, string Press = null);

/// <summary>The look of one state of a two-state starter key.</summary>
public sealed record StarterState(string Name, string Label, string SymbolId, Color Background);

/// <summary>
/// Places the keys, dials, pages, workspaces and folders of a starter profile (issue #301) on the
/// attached device. Templates address keys by grid row and column; a key outside this device's
/// grid (the fifth column on a 4×3 device) is skipped, and dials are dropped on a device without
/// any, so a template is written once and fits every device. Every page and folder gets the
/// template's wallpaper, plus matching side-display wallpapers on a device with side strips.
/// </summary>
/// <param name="theme">Name of the template's art in <c>Assets/StarterProfiles</c> (its id).</param>
/// <param name="iconStyle">Applied to every key icon; null keeps the icons plain.</param>
public sealed class StarterProfileBuilder(DeviceShape shape, bool isWindows, IStarterArt art, string theme,
    StarterIconStyle iconStyle = null)
{
    // Captions are a little larger than the actions panel's, and every icon and caption is outlined,
    // so both stay readable on the wallpapers.
    private const int CaptionSizeIncrease = 2;

    private const string WallpaperFolder = "wallpapers";
    private const string AnimationFolder = "animations";

    // Each file is stored once and its path reused for every page that shows it.
    private readonly Dictionary<string, string> _imported = new(StringComparer.Ordinal);

    /// <summary>True on Windows. The Linux variant of a template is built otherwise.</summary>
    public bool IsWindows { get; } = isWindows;

    public int Columns => shape.Geometry.Columns;

    public int Rows => shape.Geometry.Rows;

    /// <summary>True when the device has dials. Templates move their dial functions onto keys otherwise.</summary>
    public bool HasDials => DialCount > 0;

    /// <summary>Dials one dial page holds: both columns on a device with side strips.</summary>
    public int DialCount => shape.HasIndependentRotarySides
        ? shape.SideRotaryButtonCount * 2
        : shape.RotaryButtonCount;

    private int KeySize => shape.Geometry.KeySize;

    /// <summary>Adds a workspace. The first one becomes the profile's home workspace.</summary>
    public Workspace AddWorkspace(Profile profile, string name)
    {
        Workspace workspace = new() { Name = name };
        profile.Workspaces.Add(workspace);

        if (profile.Workspaces.Count == 1)
            profile.HomeWorkspaceId = workspace.Id;

        return workspace;
    }

    public TouchButtonPage AddTouchPage(Workspace workspace, string name)
    {
        TouchButtonPage page = new(shape.TouchButtonCount) { Name = name };
        ApplyWallpaper(page);
        workspace.TouchButtonPages.Add(page);
        return page;
    }

    /// <summary>
    /// Adds a folder to the workspace, or into <paramref name="parent"/>. Its bottom-left key is the
    /// Back tile the host draws, so templates leave that key free.
    /// </summary>
    public CustomFolder AddFolder(Workspace workspace, string name, CustomFolder parent = null)
    {
        CustomFolder folder = new() { Name = name, Layout = new TouchButtonPage(shape.TouchButtonCount) };
        ApplyWallpaper(folder.Layout);
        (parent?.Children ?? workspace.Folders).Add(folder);
        return folder;
    }

    /// <summary>A key with the command's icon and a caption, the way the actions panel assigns one.</summary>
    public TouchButton Key(TouchButtonPage page, int row, int column, string command, string label,
        string symbolId, Color? background = null)
    {
        TouchButton button = At(page, row, column);
        if (button == null)
            return null;

        ActionAssignment.ApplyToTouchButton(button, command, label, symbolId, KeySize, KeySize);
        if (background is { } color)
            Paint(button, color);

        StyleLayers(button);
        return button;
    }

    /// <summary>
    /// A key that shows what its display command draws (a clock, the playing track, an entity). It
    /// gets no layers of its own: the host adds the one the command draws into.
    /// </summary>
    public TouchButton Display(TouchButtonPage page, int row, int column, string command)
    {
        TouchButton button = At(page, row, column);
        if (button != null)
            button.Command = command;

        return button;
    }

    /// <summary>
    /// A key that plays one of the shipped animations (<c>anim-&lt;name&gt;.webp</c>) above a caption.
    /// </summary>
    public TouchButton Animation(TouchButtonPage page, int row, int column, string animation, string command,
        string label)
    {
        TouchButton button = At(page, row, column);
        string path = Art($"anim-{animation}.webp", AnimationFolder);
        if (button == null || path == null)
            return button;

        button.Command = command;
        button.Layers.Add(new ImageLayer
        {
            Name = label,
            AnimatedAssetPath = path,
            Scale = 0.8,
            PositionY = -(int)Math.Round(KeySize * 0.09)
        });
        button.Layers.Add(ActionAssignment.CreateCaption(label, KeySize, KeySize));
        StyleLayers(button);
        button.RewireLayerHandlers();
        return button;
    }

    /// <summary>
    /// A key with two states. <paramref name="ownedByCommand"/> builds the states a plugin command
    /// declares exactly as assigning that command in the editor does (same names, plugin-driven), so
    /// the plugin switches them; otherwise every press flips between the two states.
    /// </summary>
    public TouchButton Toggle(TouchButtonPage page, int row, int column, string command,
        StarterState first, StarterState second, bool ownedByCommand)
    {
        TouchButton button = At(page, row, column);
        if (button == null)
            return null;

        ButtonState firstState = button.States[0];
        ButtonState secondState = new();
        button.States.Add(secondState);

        foreach ((ButtonState state, StarterState spec) in new[] { (firstState, first), (secondState, second) })
        {
            state.Name = spec.Name;
            state.Command = command;
            // AddLayers writes to the active state and then rewires the button, which falls back to
            // the default state, so everything after it goes to the state object itself.
            button.SetActiveState(state.Id);
            ActionAssignment.AddLayers(button, spec.Label, spec.SymbolId, KeySize, KeySize);
            state.BackColor = spec.Background;
            state.BackgroundEnabled = true;

            // The states carry their own look, so the indicator the plugin would draw over it is
            // hidden. The host reuses this layer (same owner key) instead of adding a visible one.
            if (ownedByCommand)
            {
                state.Layers.Add(new PluginLayer
                {
                    Name = CommandStringParser.GetName(command),
                    OwnerKey = PluginLayerKey.For(command),
                    CommandName = CommandStringParser.GetName(command),
                    OwnerCreated = true,
                    Visible = false
                });
            }
        }

        StyleLayers(button);
        button.RewireLayerHandlers();

        if (ownedByCommand)
        {
            button.StateOwnerCommand = PluginLayerKey.For(command);
            button.Mode = ButtonStateMode.External;
            button.ResetOnPageChange = false;
        }
        else
        {
            firstState.Transition.Kind = StateTransitionKind.Specific;
            firstState.Transition.TargetStateId = secondState.Id;
            secondState.Transition.Kind = StateTransitionKind.Specific;
            secondState.Transition.TargetStateId = firstState.Id;
        }

        button.DefaultStateId = firstState.Id;
        button.SetActiveState(firstState.Id);
        return button;
    }

    /// <summary>
    /// Adds a dial page. On a device with side strips the dials fill the left column first, then the
    /// right; elsewhere the device takes as many as it has. Nothing is added on a device without dials.
    /// </summary>
    public void AddDialPage(Workspace workspace, string name, params StarterDial[] dials) =>
        AddDialPage(workspace, name, null, dials);

    /// <summary>
    /// Adds a dial page whose side displays play <paramref name="stripAnimation"/>
    /// (<c>anim-&lt;name&gt;.webp</c>) behind the dial labels. Only a device with side strips shows
    /// it; elsewhere this is an ordinary dial page.
    /// </summary>
    public void AddDialPage(Workspace workspace, string name, string stripAnimation, params StarterDial[] dials)
    {
        if (!HasDials)
            return;

        if (shape.HasIndependentRotarySides)
        {
            int perSide = shape.SideRotaryButtonCount;
            RotaryButtonPage left = new(perSide) { Name = name, Side = RotarySide.Left };
            RotaryButtonPage right = new(perSide) { Name = name, Side = RotarySide.Right };

            for (int i = 0; i < dials.Length && i < perSide * 2; i++)
                Fill(i < perSide ? left.RotaryButtons[i] : right.RotaryButtons[i - perSide], dials[i]);

            if (stripAnimation != null)
            {
                AnimateStrip(left, RazerStreamControllerDevice.LeftSideIndex, stripAnimation);
                AnimateStrip(right, RazerStreamControllerDevice.RightSideIndex, stripAnimation);
            }

            workspace.LeftRotaryButtonPages.Add(left);
            workspace.RightRotaryButtonPages.Add(right);
            return;
        }

        RotaryButtonPage page = new(shape.RotaryButtonCount) { Name = name, Side = RotarySide.Both };
        for (int i = 0; i < dials.Length && i < shape.RotaryButtonCount; i++)
            Fill(page.RotaryButtons[i], dials[i]);

        workspace.RotaryButtonPages.Add(page);
    }

    /// <summary>
    /// Switches a side display to free drawing: the animation fills the strip and each dial's label
    /// sits on its third, where the segmented strip would have shown it.
    /// </summary>
    private void AnimateStrip(RotaryButtonPage page, int canvasIndex, string animation)
    {
        int stripWidth = shape.Geometry.StripWidth;
        string path = Art($"anim-{animation}.webp", AnimationFolder);
        if (stripWidth <= 0 || path == null)
            return;

        TouchButton canvas = new(canvasIndex);
        canvas.Layers.Add(new ImageLayer { Name = animation, AnimatedAssetPath = path, Scale = 1.0 });

        double segment = shape.Geometry.PanelHeight / (double)RotaryButtonPage.StripSegmentCount;
        for (int i = 0; i < page.RotaryButtons.Count && i < RotaryButtonPage.StripSegmentCount; i++)
        {
            string label = page.RotaryButtons[i].DisplayText;
            if (string.IsNullOrEmpty(label))
                continue;

            canvas.Layers.Add(new TextLayer
            {
                Name = label,
                Text = label,
                Centered = true,
                Bold = true,
                Outlined = true,
                TextSize = 13,
                BoxWidth = stripWidth - 2,
                BoxHeight = (int)Math.Round(segment),
                PositionY = (int)Math.Round((i - ((RotaryButtonPage.StripSegmentCount - 1) / 2.0)) * segment)
            });
        }

        canvas.RewireLayerHandlers();
        page.StripCanvas = canvas;
        page.StripMode = StripMode.FreeDraw;
    }

    /// <summary>The template's wallpaper on the panel and, where the device has them, its side displays.</summary>
    private void ApplyWallpaper(TouchButtonPage page)
    {
        page.MainWallpaper.AssetPath = Art($"wallpaper-{theme}.png", WallpaperFolder);

        if (shape.Geometry.StripWidth <= 0)
            return;

        string strip = Art($"strip-{theme}.png", WallpaperFolder);
        page.LeftWallpaper.AssetPath = strip;
        page.RightWallpaper.AssetPath = strip;
        // Mirrored, so both side displays frame the panel symmetrically.
        page.RightWallpaper.Mirror = true;
    }

    private string Art(string fileName, string subFolder)
    {
        if (!_imported.TryGetValue(fileName, out string path))
        {
            path = art.Import(fileName, subFolder);
            _imported[fileName] = path;
        }

        return path;
    }

    private TouchButton At(TouchButtonPage page, int row, int column)
    {
        if (page == null || row < 0 || column < 0 || row >= Rows || column >= Columns)
            return null;

        int index = (row * Columns) + column;
        return index < page.TouchButtons.Count ? page.TouchButtons[index] : null;
    }

    /// <summary>
    /// Outlines every icon and caption of every state and enlarges the captions; icons also get the
    /// template's gradient and a soft shadow. Text a command writes at runtime is left alone.
    /// </summary>
    private void StyleLayers(TouchButton button)
    {
        foreach (LayerBase layer in button.States.SelectMany(state => state.Layers))
        {
            switch (layer)
            {
                case SymbolLayer symbol:
                    symbol.Outlined = true;
                    if (iconStyle == null)
                        break;

                    symbol.UseGradient = true;
                    symbol.GradientStartColor = iconStyle.GradientStart;
                    symbol.GradientEndColor = iconStyle.GradientEnd;
                    symbol.GradientAngle = 90;
                    symbol.Shadow = true;
                    break;

                case TextLayer text when !text.IsCommandOwned:
                    text.Outlined = true;
                    text.TextSize += CaptionSizeIncrease;
                    break;
            }
        }
    }

    private static void Paint(TouchButton button, Color color)
    {
        button.BackColor = color;
        button.BackgroundEnabled = true;
    }

    private static void Fill(RotaryButton dial, StarterDial spec)
    {
        dial.DisplayText = spec.Label;
        dial.RotaryLeftCommand = spec.Left ?? string.Empty;
        dial.RotaryRightCommand = spec.Right ?? string.Empty;
        dial.Command = spec.Press;
    }
}