using LoupixDeck.Localization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Models;
using LoupixDeck.Models.Converter;
using LoupixDeck.Models.Layers;
using LoupixDeck.PluginSdk;
using LoupixDeck.Registry;
using LoupixDeck.Services;
using LoupixDeck.Services.Actions;
using LoupixDeck.Services.Commands;
using LoupixDeck.Services.IconPacks;
using LoupixDeck.Services.Plugins;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;
using LoupixDeck.ViewModels.CommandPicker;
using SkiaSharp;

namespace LoupixDeck.ViewModels;

public partial class TouchButtonSettingsViewModel : DialogViewModelBase<TouchButton, DialogResult>, IAsyncInitViewModel

{
    public override void Initialize(TouchButton parameter)
    {
        if (ButtonData != null)
        {
            ButtonData.ItemChanged -= ButtonData_ItemChanged;
            ButtonData.PropertyChanged -= ButtonData_PropertyChanged;
            ButtonData.LiveContentChanged -= ButtonData_LiveContentChanged;
        }

        ButtonData = parameter;

        if (ButtonData != null)
        {
            ButtonData.ItemChanged += ButtonData_ItemChanged;
            ButtonData.PropertyChanged += ButtonData_PropertyChanged;
            ButtonData.LiveContentChanged += ButtonData_LiveContentChanged;

            // Remember the runtime active state and start editing it; restored on Cleanup so the
            // editor's state-switching does not leave the button on a non-default state at runtime.
            _originalActiveStateId = ButtonData.ActiveStateId;
            ApplyDeviceBaseToLayers();
            RefreshStateBadges();
            SelectedState = ButtonData.ActiveState;
            RebuildLayerListItems();

            SeedAnimatedLayerPreviews();
            UpdateEditorPreview();

            _selectedVibrationPattern = VibrationPatterns.FirstOrDefault(
                p => p.Value == ButtonData.VibrationPattern);
            OnPropertyChanged(nameof(SelectedVibrationPattern));
        }

        BuildCommandSlots();

        OnPropertyChanged(nameof(States));
        OnPropertyChanged(nameof(BackgroundEnabled));
        OnPropertyChanged(nameof(AreStatesLocked));
        OnPropertyChanged(nameof(CanEditStates));
        OnPropertyChanged(nameof(CanDeleteState));
        OnPropertyChanged(nameof(ResetOnPageChange));
        OnPropertyChanged(nameof(ResetOnRestart));
        OnPropertyChanged(nameof(ButtonNumber));
        OnPropertyChanged(nameof(ButtonLabel));
    }

    private readonly ICommandBuilder _commandBuilder;
    private readonly IMenuTreeBuilder _menuTreeBuilder;
    private readonly ICommandRegistry _commandRegistry;
    private readonly Services.Companion.ICommandLockService _commandLock;
    private readonly Services.Commands.ICommandStateMaterializer _stateMaterializer;
    private readonly IAssetService _assetService;
    private readonly IDialogService _dialogService;
    private readonly IButtonTemplateService _buttonTemplates;
    private readonly ISideStripProviderRegistry _sideStripRegistry;
    private readonly IDynamicTextManager _dynamicTextManager;
    private readonly Services.Animation.IButtonAnimationManager _buttonAnimationManager;
    private readonly Services.Animation.IAnimatedImageImporter _animatedImageImporter;
    private readonly Services.Animation.IAnimatedImageCache _animatedImageCache;
    private readonly Services.AppLauncher.IAppIconExtractor _appIcons;
    private readonly LoupedeckConfig _config;

    /// <summary>
    /// Smallest working margin (canvas px) kept around the frame on every side, so a
    /// layer dragged past the button edge stays visible and grabbable. The canvas grows
    /// past this to fill the viewport — see <see cref="EditorCanvasWidth"/>.
    /// </summary>
    public const int EditorCanvasBleed = 75;

    // Device-pixel dimensions of the edited surface. 90×90 for grid touch buttons;
    /// <summary>False on a device without a haptic motor — the per-button vibration
    /// controls are hidden rather than shown doing nothing.</summary>
    public bool IsVibrationSupported { get; }

    // set to the side-strip size for a Razer free-draw canvas via SetCanvasSize.
    public int DeviceWidth { get; private set; } = DeviceGeometry.Default.KeySize;
    public int DeviceHeight { get; private set; } = DeviceGeometry.Default.KeySize;

    /// <summary>Editor → device coordinate factor (canvas pixels per device pixel).</summary>
    public double EditorToDeviceScale => BitmapHelper.ComputeEditorFrame(DeviceWidth, DeviceHeight).Scale;

    /// <summary>Rendered frame size (canvas px), aspect-correct for the device surface.</summary>
    public double FrameWidth => BitmapHelper.ComputeEditorFrame(DeviceWidth, DeviceHeight).FrameWidth;
    public double FrameHeight => BitmapHelper.ComputeEditorFrame(DeviceWidth, DeviceHeight).FrameHeight;

    /// <summary>
    /// Editor canvas size (canvas px): the frame plus at least <see cref="EditorCanvasBleed"/>
    /// on each side, grown to fill the preview viewport at the current zoom. Filling the
    /// viewport is what keeps the panel free of dead grey bars around the canvas — every
    /// pixel the user sees is drawable, so a layer dragged off the button stays visible
    /// instead of vanishing where the canvas ended.
    /// </summary>
    public int EditorCanvasWidth => CanvasExtent(FrameWidth, _viewport.Width);
    public int EditorCanvasHeight => CanvasExtent(FrameHeight, _viewport.Height);

    private int CanvasExtent(double frameExtent, double viewportExtent)
    {
        double minimum = frameExtent + (2.0 * EditorCanvasBleed);

        // Floor, so the zoomed content never exceeds the viewport by a rounding pixel and
        // trips the scrollbar. The viewport is only known once the View has measured.
        //
        // The zoom divisor is clamped at 1 so zooming out cannot inflate the canvas — and
        // with it the rendered bitmap, which is re-rendered on every drag step — past the
        // panel's own size. Below 100% the canvas therefore stays put and the view simply
        // shows the workspace smaller.
        double fill = viewportExtent > 0
            ? Math.Floor(viewportExtent / Math.Max(1.0, ZoomFactor))
            : 0;

        return (int)Math.Round(Math.Max(minimum, fill));
    }

    /// <summary>Top-left of the centered frame inside the editor canvas.</summary>
    public double FrameOffsetX => (EditorCanvasWidth - FrameWidth) / 2.0;
    public double FrameOffsetY => (EditorCanvasHeight - FrameHeight) / 2.0;

    /// <summary>
    /// Sets the edited surface's device-pixel dimensions (e.g. the side-strip size for a
    /// free-draw canvas). Call before <see cref="Initialize"/>. Defaults to the attached
    /// device's key size.
    /// </summary>
    public void SetCanvasSize(int deviceWidth, int deviceHeight)
    {
        DeviceWidth = Math.Max(1, deviceWidth);
        DeviceHeight = Math.Max(1, deviceHeight);
        ApplyDeviceBaseToLayers();
        OnPropertyChanged(nameof(DeviceWidth));
        OnPropertyChanged(nameof(DeviceHeight));
        OnPropertyChanged(nameof(EditorToDeviceScale));
        OnPropertyChanged(nameof(FrameWidth));
        OnPropertyChanged(nameof(FrameHeight));
        OnPropertyChanged(nameof(EditorCanvasWidth));
        OnPropertyChanged(nameof(EditorCanvasHeight));
        OnPropertyChanged(nameof(FrameOffsetX));
        OnPropertyChanged(nameof(FrameOffsetY));
        OnPropertyChanged(nameof(CanvasSizeText));
        UpdateEditorPreview();
        UpdateSelectionBounds();
    }

    /// <summary>
    /// Tells every layer of the edited button how big the surface it is drawn onto is, so the
    /// editor's size fields report real device pixels. Purely a runtime projection — nothing
    /// here is persisted.
    /// </summary>
    private void ApplyDeviceBaseToLayers()
    {
        if (ButtonData?.Layers == null) return;
        foreach (LayerBase layer in ButtonData.Layers)
        {
            if (layer == null) continue;
            layer.DeviceBaseWidth = DeviceWidth;
            layer.DeviceBaseHeight = DeviceHeight;
        }
    }

    /// <summary>Adds a newly created layer, stamped with the surface size like the existing ones.</summary>
    private void AddLayer(LayerBase layer)
    {
        layer.DeviceBaseWidth = DeviceWidth;
        layer.DeviceBaseHeight = DeviceHeight;
        PlaceInEditedSegment(layer);

        // On the all-segments surface a new layer goes behind the segments, on top of the layers
        // already there (right under the segments row of the list). The canvas keeps the layers
        // behind the segments first, so that is the index after the last of them.
        if (IsEditingSharedSurface)
        {
            SortSharedLayers();
            layer.BehindSegments = true;
            int behind = 0;
            while (behind < ButtonData.Layers.Count && ButtonData.Layers[behind].BehindSegments)
                behind++;
            ButtonData.Layers.Insert(behind, layer);
            return;
        }

        ButtonData.Layers.Add(layer);
    }

    /// <summary>
    /// A new layer on a segment of a split wheel starts in the middle of that segment instead of
    /// the screen centre, which lies on the dividers; a text box is sized to fit the segment.
    /// </summary>
    private void PlaceInEditedSegment(LayerBase layer)
    {
        if (EditedSegment == null || layer.PositionX != 0 || layer.PositionY != 0) return;

        var mode = _wheel.SplitMode;
        var centre = WheelSplitGeometry.Centroid(mode, _wheelSegmentIndex, DeviceWidth);
        layer.PositionX = (int)Math.Round(centre.X - DeviceWidth / 2f);
        layer.PositionY = (int)Math.Round(centre.Y - DeviceHeight / 2f);

        if (layer is TextLayer text)
        {
            float share = WheelSplitGeometry.SegmentCount(mode) > 2 ? 0.3f : 0.45f;
            text.BoxWidth = (int)Math.Round(DeviceWidth * share);
            text.BoxHeight = (int)Math.Round(DeviceHeight * share * 0.6f);
        }
    }

    // ───────── Editor zoom ─────────

    public const double MinZoom = 0.25;
    public const double MaxZoom = 4.0;

    /// <summary>Uniform scale applied to the editor canvas (via a LayoutTransform, so the
    /// children keep their unscaled local coordinates and the pointer math is unaffected).</summary>
    public double ZoomFactor
    {
        get;
        private set
        {
            var clamped = Math.Clamp(value, MinZoom, MaxZoom);
            if (Math.Abs(field - clamped) < 0.0001) return;
            field = clamped;
            OnPropertyChanged(nameof(ZoomFactor));
            OnPropertyChanged(nameof(ZoomPercentText));

            // The canvas is sized to fill the viewport at the current zoom, so it has to
            // be re-measured and re-rendered whenever the zoom changes.
            RefreshCanvasExtent();
        }
    } = 1.0;

    public string ZoomPercentText => $"{Math.Round(ZoomFactor * 100)}%";

    // Viewport of the scroll area, pushed from the View so Fit can size to it.
    private Avalonia.Size _viewport;

    /// <summary>Called by the View when the preview viewport is measured/resized so the
    /// Fit command knows the available space. Opening does NOT auto-zoom — every canvas
    /// (touch button or side strip) opens at 100%; the user fits/zooms manually.</summary>
    public void SetViewport(double width, double height)
    {
        if (width <= 0 || height <= 0) return;
        if (Math.Abs(_viewport.Width - width) < 0.5 && Math.Abs(_viewport.Height - height) < 0.5) return;

        _viewport = new Avalonia.Size(width, height);

        // The canvas fills the viewport, so a resized panel means a resized canvas.
        RefreshCanvasExtent();
    }

    /// <summary>
    /// Re-publishes the canvas geometry and repaints the preview after something that
    /// changes the canvas extent (zoom or viewport). The frame keeps its size — only the
    /// drawable area around it grows or shrinks.
    /// </summary>
    private void RefreshCanvasExtent()
    {
        OnPropertyChanged(nameof(EditorCanvasWidth));
        OnPropertyChanged(nameof(EditorCanvasHeight));
        OnPropertyChanged(nameof(FrameOffsetX));
        OnPropertyChanged(nameof(FrameOffsetY));
        UpdateEditorPreview();
        UpdateSelectionBounds();
    }

    private void FitToViewport()
    {
        if (_viewport.Width <= 0 || _viewport.Height <= 0) return;
        // Fit the frame, not the canvas: the canvas always fills the viewport, so fitting
        // it would be a no-op. 0.92 leaves a little of the surrounding work area visible.
        var fw = FrameWidth;
        var fh = FrameHeight;
        if (fw <= 0 || fh <= 0) return;
        const double pad = 0.92;
        var fit = Math.Min(_viewport.Width * pad / fw, _viewport.Height * pad / fh);
        ZoomFactor = fit;
    }

    private void ZoomIn() => ZoomFactor *= 1.25;
    private void ZoomOut() => ZoomFactor /= 1.25;
    private void ResetZoom() => ZoomFactor = 1.0;
    private void Fit() => FitToViewport();

    // ───────── Side-strip (Razer) mode ─────────

    private RotaryButtonPage _stripPage;

    /// <summary>
    /// True when this editor instance is editing a Razer side-strip canvas (rather
    /// than an ordinary grid touch button). Drives the strip-mode picker and the
    /// draw-mode gate; false for normal buttons, so their behaviour is unchanged.
    /// </summary>
    public bool IsStripCanvas => _stripPage != null;

    /// <summary>Strip modes offered in the editor's picker.</summary>
    public IReadOnlyList<StripMode> AvailableStripModes { get; } =
        new[] { StripMode.Segmented, StripMode.FreeDraw, StripMode.PluginOverride };

    /// <summary>Plugin side-strip providers bindable in PluginOverride mode.</summary>
    public IReadOnlyList<ISideStripProvider> AvailableStripProviders => _sideStripRegistry.Providers;

    /// <summary>True while editing a strip whose mode is PluginOverride — shows the
    /// provider picker.</summary>
    public bool IsPluginOverride => IsStripCanvas && StripMode == StripMode.PluginOverride;

    /// <summary>
    /// The provider bound to this page in PluginOverride mode. Reads/writes
    /// <see cref="RotaryButtonPage.StripPluginId"/> by id. Setting it repaints the strip
    /// live via the canvas refresh subscription.
    /// </summary>
    public ISideStripProvider SelectedStripProvider
    {
        get => _stripPage == null ? null : _sideStripRegistry.Get(_stripPage.StripPluginId);
        set
        {
            if (_stripPage == null) return;
            var id = value?.Id;
            if (_stripPage.StripPluginId == id) return;
            _stripPage.StripPluginId = id;
            OnPropertyChanged();
            _stripPage.StripCanvas?.Refresh();
        }
    }

    /// <summary>
    /// The edited side strip's per-page <see cref="StripMode"/>. Writes straight
    /// through to the owning <see cref="RotaryButtonPage"/>. Segmented shows the dial
    /// labels; FreeDraw shows (and allows editing of) this page's canvas.
    /// </summary>
    public StripMode StripMode
    {
        get => _stripPage?.StripMode ?? StripMode.Segmented;
        set
        {
            if (_stripPage == null || _stripPage.StripMode == value) return;
            _stripPage.StripMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanEditCanvas));
            OnPropertyChanged(nameof(IsDrawDisabledHintVisible));
            OnPropertyChanged(nameof(IsPluginOverride));
            OnPropertyChanged(nameof(SelectedStripProvider));

            OnPropertyChanged(nameof(IsSegmentCommandMode));
            OnPropertyChanged(nameof(ShowCommandArea));

            // A tab that disappears under the selection would leave the panel blank.
            if (!ShowCommandArea && RightTabIndex is CommandsTabIndex or AppsTabIndex)
                RightTabIndex = 0;

            // The bottom area shows a single command sequence for a normal button / non-FreeDraw
            // strip, and three (top/middle/bottom) for a FreeDraw strip — rebuild for the new mode.
            BuildCommandSlots();

            // Repaint the preview so the segment dividers appear/disappear, and the strip on
            // the device via the canvas's live-redraw subscription (the controller reads the new
            // mode and renders labels vs. canvas), instead of waiting for the dialog to close.
            UpdateEditorPreview();
            _stripPage.StripCanvas?.Refresh();
        }
    }

    // ───────── Command sequences (single, or three FreeDraw segments) ─────────

    /// <summary>True when the bottom area edits a FreeDraw strip's three per-segment command
    /// sequences (top/middle/bottom) instead of the single <see cref="TouchButton.Command"/>.</summary>
    public bool IsSegmentCommandMode => IsStripCanvas && StripMode == StripMode.FreeDraw;

    private static readonly string[] SegmentTitleKeys = ["Slot_TopSegment", "Slot_MiddleSegment", "Slot_BottomSegment"];

    /// <summary>The editable command sequence strips: one (<see cref="TouchButton.Command"/>)
    /// for a normal button / non-FreeDraw strip, or three (the FreeDraw segments) bound to the
    /// page's <see cref="RotaryButtonPage.StripSegmentCommands"/>.</summary>
    public ObservableCollection<CommandSequenceSlot> CommandSlots { get; } = [];

    /// <summary>The slot a double-clicked tree command appends to; set by clicking a strip.</summary>
    public CommandSequenceSlot ActiveSlot { get; private set; }

    /// <summary>Marks <paramref name="slot"/> as the active double-click target.</summary>
    public void SetActiveSlot(CommandSequenceSlot slot)
    {
        if (slot == null || ReferenceEquals(ActiveSlot, slot)) return;
        ActiveSlot = slot;
        foreach (var s in CommandSlots)
            s.IsActive = ReferenceEquals(s, slot);
        OnPropertyChanged(nameof(ActiveSlot));
    }

    /// <summary>Appends a command (double-click in the tree) to the active slot.</summary>
    public void InsertCommand(MenuEntry menuEntry) => ActiveSlot?.InsertCommand(menuEntry);

    /// <summary>
    /// Gives a button that has no artwork yet the layers its first command declares — the same result
    /// the actions panel produces for it. Commands without a declared layout change nothing. A button
    /// that already has layers is left alone: the user's work
    /// is never replaced by picking a command.
    /// </summary>
    private void OnMainCommandInserted(MenuEntry menuEntry, bool wasEmpty)
    {
        if (!wasEmpty || ButtonData?.Layers is not { Count: 0 } || !CanEditCanvas)
            return;

        var registered = _commandRegistry.Get(menuEntry.Command);

        // Only a command that declares its own layout gets layers here; every other command leaves
        // the button as it was, which is how the editor has always behaved.
        var layout = registered?.Info?.ButtonLayout;
        if (layout == null)
            return;

        // The entry the editor's picker hands over does not always carry the glyph, so the command's
        // own declaration is the fallback.
        var glyph = string.IsNullOrEmpty(menuEntry.Icon) ? registered.Info.Icon : menuEntry.Icon;
        var symbolId = SymbolLibrary.TryGetByGlyph(glyph, out var definition) ? definition.Id : string.Empty;

        ActionAssignment.AddLayers(ButtonData, menuEntry.Name, symbolId, DeviceWidth, DeviceHeight, layout, _assetService);
    }

    /// <summary>(Re)builds the command sequence slots for the current mode and selects the first.</summary>
    private void BuildCommandSlots()
    {
        foreach (var slot in CommandSlots)
            slot.Cleanup();
        CommandSlots.Clear();
        ActiveSlot = null;

        if (ButtonData == null) return;

        // The wheel canvas has no press command of its own; the wheel's gestures live on the
        // RotaryButton and are edited in the rotary editor.
        if (IsWheelCanvas)
        {
            RefreshParameterSegments();
            return;
        }

        // A Segmented or PluginOverride strip never runs the canvas command, so it gets no slot.
        if (IsStripCanvas && !IsSegmentCommandMode)
        {
            RefreshParameterSegments();
            return;
        }

        if (IsSegmentCommandMode && _stripPage != null)
        {
            for (var i = 0; i < RotaryButtonPage.StripSegmentCount; i++)
            {
                var index = i;
                CommandSlots.Add(new CommandSequenceSlot(
                    Loc.Tr(SegmentTitleKeys[index]), _commandBuilder, _commandRegistry, _commandLock, _dialogService,
                    () => _stripPage.GetStripSegmentCommand(index),
                    v => _stripPage.SetStripSegmentCommand(index, string.IsNullOrWhiteSpace(v) ? null : v)));
                CommandSlots[^1].Commands.CollectionChanged += (_, _) => RefreshParameterSegments();
            }
        }
        else
        {
            var slot = new CommandSequenceSlot(
                Loc.Tr("Slot_CommandSequence"), _commandBuilder, _commandRegistry, _commandLock, _dialogService,
                () => ButtonData.Command,
                v => ButtonData.Command = string.IsNullOrWhiteSpace(v) ? null : v);
            slot.CommandInserted += OnMainCommandInserted;
            slot.Commands.CollectionChanged += (_, _) => RefreshParameterSegments();
            CommandSlots.Add(slot);
        }

        if (CommandSlots.Count > 0)
            SetActiveSlot(CommandSlots[0]);

        RefreshParameterSegments();
    }

    /// <summary>
    /// Whether the canvas and its layers may be edited. Always true for normal touch
    /// buttons; for a side strip only while it is in <see cref="StripMode.FreeDraw"/>
    /// — in Segmented mode the strip renders the dial labels, so its canvas is locked.
    /// </summary>
    public bool CanEditCanvas => !IsStripCanvas || StripMode == StripMode.FreeDraw;

    /// <summary>Shows the "switch to FreeDraw" hint while a strip's editing is locked.</summary>
    public bool IsDrawDisabledHintVisible => IsStripCanvas && StripMode != StripMode.FreeDraw;

    /// <summary>
    /// Marks this editor as editing the side-strip canvas of <paramref name="page"/>,
    /// enabling the strip-mode picker and the draw-mode gate. Call before
    /// <see cref="Initialize"/>.
    /// </summary>
    public void ConfigureStrip(RotaryButtonPage page)
    {
        _stripPage = page;
        OnPropertyChanged(nameof(IsStripCanvas));
        OnPropertyChanged(nameof(ShowStatesSection));
        OnPropertyChanged(nameof(StripMode));
        OnPropertyChanged(nameof(CanEditCanvas));
        OnPropertyChanged(nameof(IsDrawDisabledHintVisible));
        OnPropertyChanged(nameof(IsPluginOverride));
        OnPropertyChanged(nameof(AvailableStripProviders));
        OnPropertyChanged(nameof(SelectedStripProvider));
        OnPropertyChanged(nameof(IsSegmentCommandMode));
        OnPropertyChanged(nameof(ShowButtonSection));
        OnPropertyChanged(nameof(ShowCommandArea));
    }

    private RotaryButton _wheel;

    /// <summary>True while editing a CT wheel mode's screen canvas (<see cref="RotaryButton.Canvas"/>).</summary>
    public bool IsWheelCanvas => _wheel != null;

    /// <summary>
    /// Set when the user chose "Use automatic layout": the caller drops the canvas so the wheel
    /// goes back to the built-in rendering. The canvas object itself is left untouched here.
    /// </summary>
    public bool RevertToAutoLayout { get; private set; }

    /// <summary>
    /// True once the edited surface has changed while the editor was open (a layer added, moved
    /// or restyled, the background toggled, ...). Lets the caller tell "opened and closed again"
    /// from a real edit.
    /// </summary>
    public bool HasEdits { get; private set; }

    /// <summary>
    /// Marks this editor as editing <paramref name="wheel"/>'s screen canvas: no states, no
    /// commands (the wheel's gestures are edited in the rotary editor), a round preview mask, and
    /// a sample dial value so indicator and dial-text layers show while styling. Call after
    /// <see cref="SetCanvasSize"/> and before <see cref="Initialize"/>.
    /// </summary>
    public void ConfigureWheel(RotaryButton wheel)
    {
        _wheel = wheel;
        _wheelSegmentIndex = wheel.IsSplit ? 0 : -1;
        WheelSplitOptions = WheelSplitOption.All();
        _suppressWheelSplitChange = true;
        SelectedWheelSplit = WheelSplitOptions.FirstOrDefault(o => o.Mode == wheel.SplitMode);
        _suppressWheelSplitChange = false;
        RebuildWheelSurfaceChoices();
        OnPropertyChanged(nameof(WheelSplitOptions));
        OnPropertyChanged(nameof(ShowWheelDividers));
        OnPropertyChanged(nameof(WheelDividerIcon));
        OnPropertyChanged(nameof(IsWheelSplit));
        OnPropertyChanged(nameof(ShowUseAutomaticLayout));
        OnPropertyChanged(nameof(IsWheelCanvas));
        OnPropertyChanged(nameof(ShowStatesSection));
        OnPropertyChanged(nameof(ShowButtonSection));
        OnPropertyChanged(nameof(ShowCommandArea));
        OnPropertyChanged(nameof(ShowParametersTab));
        OnPropertyChanged(nameof(ButtonLabel));
        OnPropertyChanged(nameof(TextSources));
    }

    /// <summary>
    /// Surface of the wheel being edited: a segment index of a split wheel, <see cref="SharedSurface"/>
    /// for the layers shown on all segments, or -1 for the whole screen of an unsplit wheel.
    /// </summary>
    private int _wheelSegmentIndex = -1;

    /// <summary><see cref="_wheelSegmentIndex"/> while the split wheel's all-segments surface is edited.</summary>
    private const int SharedSurface = -2;

    /// <summary>The segment being edited, or null for the whole wheel screen or the all-segments surface.</summary>
    private WheelSegment EditedSegment =>
        _wheel != null && _wheelSegmentIndex >= 0 && _wheelSegmentIndex < _wheel.ActiveSegments().Count
            ? _wheel.ActiveSegments()[_wheelSegmentIndex]
            : null;

    /// <summary>True while the split wheel's all-segments surface (<see cref="RotaryButton.SplitCanvas"/>) is edited.</summary>
    private bool IsEditingSharedSurface => IsWheelSplit && _wheelSegmentIndex == SharedSurface;

    // ───────── Split wheel ─────────

    /// <summary>
    /// Gives a split wheel everything the editor edits: a canvas for each active segment (a
    /// segment shows nothing until it has one) and the all-segments surface. The canvas of an
    /// unsplit wheel is owned by the caller (<see cref="EnsureWheelCanvas"/>).
    /// </summary>
    public static void EnsureSegmentCanvases(RotaryButton wheel)
    {
        var segments = wheel.ActiveSegments();
        for (int i = 0; i < segments.Count; i++)
            segments[i].Canvas ??= WheelCanvasDefaults.CreateSegment(i);
        wheel.SplitCanvas ??= WheelCanvasDefaults.CreateSplitShared();
    }

    /// <summary>
    /// Asked when the wheel's split changed in the editor, so the caller can register the new
    /// canvases with the live redraw and the dynamic-content managers. Optional.
    /// </summary>
    public Action WheelCanvasesChanged { get; set; }

    /// <summary>
    /// Asked for the wheel's own canvas when the editor switches it back to unsplit and it has
    /// none. Set by the caller, which creates and tracks it. Optional; without it no canvas is made.
    /// </summary>
    public Func<TouchButton> EnsureWheelCanvas { get; set; }

    /// <summary>The split modes offered for the wheel screen.</summary>
    public IReadOnlyList<WheelSplitOption> WheelSplitOptions { get; private set; } = [];

    /// <summary>True while a split wheel is edited: the surface selector shows.</summary>
    public bool IsWheelSplit => IsWheelCanvas && _wheel.IsSplit;

    /// <summary>
    /// Whether the split wheel draws the lines between its segments, on the device and in the
    /// preview (<see cref="RotaryButton.ShowSplitDividers"/>).
    /// </summary>
    public bool ShowWheelDividers
    {
        get => _wheel?.ShowSplitDividers ?? true;
        set
        {
            if (_wheel == null || _wheel.ShowSplitDividers == value) return;
            _wheel.ShowSplitDividers = value;
            HasEdits = true;
            OnPropertyChanged(nameof(ShowWheelDividers));
            UpdateEditorPreview();
            WheelCanvasesChanged?.Invoke();
        }
    }

    /// <summary>Icon of the divider toggle: the current split's dividers.</summary>
    public Geometry WheelDividerIcon => WheelZoneIcons.Dividers(_wheel?.SplitMode ?? Models.WheelSplitMode.None);

    /// <summary>"Use automatic layout" only applies to an unsplit wheel; a split one has no automatic look.</summary>
    public bool ShowUseAutomaticLayout => IsWheelCanvas && !_wheel.IsSplit;

    /// <summary>Stands for the segments in the all-segments surface's layer list (see <see cref="SharedSurfaceLayerView"/>).</summary>
    private readonly WheelSegmentsMarker _segmentsMarker = new() { Name = Loc.Tr("Wheel_SegmentsMarker") };

    /// <summary>Segment under the pointer in the preview, highlighted so a click's target shows; -1 for none.</summary>
    private int _hoverSegment = -1;

    /// <summary>Highlights the segment under a point of the editor canvas (editor pixels) in the preview.</summary>
    public void UpdateWheelHover(double editorX, double editorY) => SetWheelHover(WheelSegmentAt(editorX, editorY));

    /// <summary>Removes the hover highlight, e.g. when the pointer leaves the preview.</summary>
    public void ClearWheelHover() => SetWheelHover(-1);

    private void SetWheelHover(int segment)
    {
        if (segment == _hoverSegment) return;
        _hoverSegment = segment;
        UpdateEditorPreview();
    }

    /// <summary>The segment of a split wheel under a point of the editor canvas, or -1.</summary>
    private int WheelSegmentAt(double editorX, double editorY)
    {
        if (!IsWheelSplit) return -1;

        var (scale, frameW, frameH) = BitmapHelper.ComputeEditorFrame(DeviceWidth, DeviceHeight);
        double x = (editorX - ((EditorCanvasWidth - frameW) / 2.0)) / scale;
        double y = (editorY - ((EditorCanvasHeight - frameH) / 2.0)) / scale;
        if (x < 0 || y < 0 || x > DeviceWidth || y > DeviceHeight) return -1;

        // Only the round glass counts: the corners of the square show nothing.
        double r = DeviceWidth / 2.0;
        if (((x - r) * (x - r)) + ((y - r) * (y - r)) > r * r) return -1;

        return WheelSplitGeometry.HitTest(_wheel.SplitMode, (int)x, (int)y, DeviceWidth);
    }

    // Set while ConfigureWheel picks the current option, so that does not count as a change.
    private bool _suppressWheelSplitChange;

    /// <summary>
    /// The wheel's split mode. Changing it splits the wheel at once (keeping segments a mode with
    /// fewer of them hides) and switches the editor to the first segment, or back to the whole
    /// wheel screen.
    /// </summary>
    [ObservableProperty]
    public partial WheelSplitOption SelectedWheelSplit { get; set; }

    partial void OnSelectedWheelSplitChanged(WheelSplitOption value)
    {
        if (_suppressWheelSplitChange || value == null || _wheel == null || _wheel.SplitMode == value.Mode) return;

        _wheel.SplitMode = value.Mode;
        HasEdits = true;

        TouchButton canvas;
        if (_wheel.IsSplit)
        {
            EnsureSegmentCanvases(_wheel);
            canvas = _wheel.ActiveSegments()[0].Canvas;
        }
        else
        {
            canvas = _wheel.Canvas ?? EnsureWheelCanvas?.Invoke();
        }

        // The index first, so the surface choice rebuilt below does not switch canvases itself.
        _wheelSegmentIndex = _wheel.IsSplit ? 0 : -1;
        RebuildWheelSurfaceChoices();
        OnPropertyChanged(nameof(IsWheelSplit));
        OnPropertyChanged(nameof(WheelDividerIcon));
        OnPropertyChanged(nameof(ShowUseAutomaticLayout));
        WheelCanvasesChanged?.Invoke();

        if (canvas != null)
            SwitchEditedCanvas(canvas, _wheel.IsSplit ? 0 : -1);
    }

    /// <summary>
    /// The surfaces of a split wheel the editor can work on: first "All segments", then the
    /// segments in segment order.
    /// </summary>
    public IReadOnlyList<WheelSurfaceChoice> WheelSurfaceChoices { get; private set; } = [];

    /// <summary>The selected entry of <see cref="WheelSurfaceChoices"/>: 0 = all segments, n = segment n - 1.</summary>
    [ObservableProperty]
    public partial int SelectedWheelSurface { get; set; } = -1;

    partial void OnSelectedWheelSurfaceChanged(int value)
    {
        if (!IsWheelSplit || value < 0) return;

        int index = value == 0 ? SharedSurface : value - 1;
        if (index == _wheelSegmentIndex) return;

        EnsureSegmentCanvases(_wheel);
        var segments = _wheel.ActiveSegments();
        if (index == SharedSurface)
            SwitchEditedCanvas(_wheel.SplitCanvas, SharedSurface);
        else if (index < segments.Count)
            SwitchEditedCanvas(segments[index].Canvas, index);
    }

    /// <summary>
    /// Selects the segment under a point of the editor canvas (in editor pixels), for a click on
    /// empty canvas. True when a segment was hit.
    /// </summary>
    public bool TrySelectWheelSegmentAt(double editorX, double editorY)
    {
        int index = WheelSegmentAt(editorX, editorY);
        if (index < 0) return false;

        SelectedWheelSurface = index + 1;
        return true;
    }

    private void RebuildWheelSurfaceChoices()
    {
        var mode = _wheel?.SplitMode ?? Models.WheelSplitMode.None;
        WheelSurfaceChoices = IsWheelSplit
            ? [new WheelSurfaceChoice(Loc.Tr("Wheel_AllSegments"), WheelZoneIcons.Whole(), WheelZoneIcons.Lines(mode)),
                .. Enumerable.Range(0, WheelSplitGeometry.SegmentCount(mode))
                    .Select(i => new WheelSurfaceChoice(Loc.Tr(WheelSplitGeometry.SegmentNameKey(mode, i)),
                        WheelZoneIcons.Fill(mode, i), WheelZoneIcons.Lines(mode)))]
            : [];
        OnPropertyChanged(nameof(WheelSurfaceChoices));
        SelectedWheelSurface = SurfaceChoiceOf(_wheelSegmentIndex);
    }

    /// <summary>The <see cref="WheelSurfaceChoices"/> entry of an edited surface index.</summary>
    private int SurfaceChoiceOf(int segmentIndex) => !IsWheelSplit
        ? -1
        : segmentIndex == SharedSurface ? 0 : Math.Max(segmentIndex, 0) + 1;

    /// <summary>
    /// Makes <paramref name="canvas"/> the edited surface: the wheel's own canvas
    /// (<paramref name="segmentIndex"/> -1), a segment's, or the all-segments surface
    /// (<see cref="SharedSurface"/>). Edits made so far stay on the canvas they were made on; the
    /// layer list, selection and preview follow the new one.
    /// </summary>
    private void SwitchEditedCanvas(TouchButton canvas, int segmentIndex)
    {
        _wheelSegmentIndex = segmentIndex;
        _hoverSegment = -1;
        SelectedLayer = null;
        Initialize(canvas);
        SelectedWheelSurface = SurfaceChoiceOf(segmentIndex);
        OnPropertyChanged(nameof(ButtonLabel));
    }

    /// <summary>Sample value the wheel preview is rendered with, so the arc and value text are visible.</summary>
    private static readonly AdjustmentValue WheelPreviewValue = new(0.65, "65 %");

    /// <summary>Sample value a touch key preview is rendered with when the key has no live value.</summary>
    private static readonly AdjustmentValue KeyPreviewValue = new(0.65, "65 %") { Detail = "Detail" };

    /// <summary>
    /// The dial context for the preview. The wheel: its real label (or a sample) and a sample value.
    /// A touch key: the value its command last reported, or a sample, so indicator and value text
    /// layers show while styling.
    /// </summary>
    private DialRenderContext? PreviewDial => IsWheelCanvas
        ? new DialRenderContext(
            WheelPreviewLabel(_wheel.DisplayText),
            WheelPreviewValue)
        : IsStripCanvas
            ? null
            : new DialRenderContext(null, ButtonData?.DisplayValue ?? KeyPreviewValue);

    private static readonly IReadOnlyList<TextSource> WheelTextSources = Enum.GetValues<TextSource>();

    // A touch key has no wheel label; it gets its value and detail from its command.
    private static readonly IReadOnlyList<TextSource> KeyTextSources =
        [TextSource.Static, TextSource.DialValue, TextSource.ValueDetail];

    /// <summary>The text sources a text layer on the edited surface can pick from.</summary>
    public IReadOnlyList<TextSource> TextSources => IsWheelCanvas ? WheelTextSources : KeyTextSources;

    public IRelayCommand AddLiveImageLayerCommand => field ??= Relay.Create(AddLiveImageLayer);
    public IRelayCommand AddIndicatorLayerCommand => field ??= Relay.Create(AddIndicatorLayer);
    public IRelayCommand UseAutomaticLayoutCommand => field ??= Relay.Create(UseAutomaticLayout);

    private void AddIndicatorLayer()
    {
        var layer = new DialIndicatorLayer { Name = GetUniqueLayerName("Indicator") };
        AddLayer(layer);
        SelectedLayer = layer;
    }

    private void UseAutomaticLayout()
    {
        RevertToAutoLayout = true;
        DialogResult.TrySetResult(new DialogResult(true));
    }

    /// <summary>Spacing of the editor's alignment grid in device pixels; also the
    /// step used when <see cref="SnapToGrid"/> is active.</summary>
    public const int GridStepDevice = 10;

    private bool _showGrid;

    /// <summary>Toggles the alignment grid overlay in the preview canvas.</summary>
    public bool ShowGrid
    {
        get => _showGrid;
        set
        {
            if (_showGrid == value) return;
            _showGrid = value;
            OnPropertyChanged(nameof(ShowGrid));
            UpdateEditorPreview();
        }
    }

    private bool _snapToGrid;

    /// <summary>When enabled, dragging a layer snaps its top-left edge to the grid.</summary>
    public bool SnapToGrid
    {
        get => _snapToGrid;
        set
        {
            if (_snapToGrid == value) return;
            _snapToGrid = value;
            OnPropertyChanged(nameof(SnapToGrid));
        }
    }

    public IAsyncRelayCommand AddImageLayerCommand => field ??= Relay.Create(AddImageLayer);
    public IAsyncRelayCommand AddAnimatedImageLayerCommand => field ??= Relay.Create(AddAnimatedImageLayer);
    public IRelayCommand AddTextLayerCommand => field ??= Relay.Create(AddTextLayer);
    public IAsyncRelayCommand ApplyTemplateCommand => field ??= Relay.Create<ButtonTemplate>(ApplyTemplate);
    public IAsyncRelayCommand AddSymbolLayerCommand => field ??= Relay.Create(AddSymbolLayer);

    public IRelayCommand RemoveLayerCommand => field ??= Relay.Create(RemoveSelectedLayer);
    public IRelayCommand MoveLayerUpCommand => field ??= Relay.Create(MoveSelectedLayerUp);
    public IRelayCommand MoveLayerDownCommand => field ??= Relay.Create(MoveSelectedLayerDown);

    public IRelayCommand ZoomInCommand => field ??= Relay.Create(ZoomIn);
    public IRelayCommand ZoomOutCommand => field ??= Relay.Create(ZoomOut);
    public IRelayCommand ResetZoomCommand => field ??= Relay.Create(ResetZoom);
    public IRelayCommand FitCommand => field ??= Relay.Create(Fit);

    public TouchButton ButtonData { get; set; }

    /// <summary>1-based button number shared by the window title and the
    /// properties panel so both read identically; the underlying Index stays
    /// 0-based.</summary>
    public int ButtonNumber => (ButtonData?.Index ?? 0) + 1;

    /// <summary>Window title, e.g. "Touch Button 1" — or the wheel's own title for its canvas.</summary>
    public string ButtonLabel => IsWheelCanvas
        ? EditedSegment != null
            ? Loc.Tr("Wheel_SegmentTitleFmt", Loc.Tr("TouchButton_CentreWheelTitle"),
                Loc.Tr(WheelSplitGeometry.SegmentNameKey(_wheel.SplitMode, _wheelSegmentIndex)))
            : IsEditingSharedSurface
                ? Loc.Tr("Wheel_SegmentTitleFmt", Loc.Tr("TouchButton_CentreWheelTitle"), Loc.Tr("Wheel_AllSegments"))
                : Loc.Tr("TouchButton_CentreWheelTitle")
        : $"Touch Button {ButtonNumber}";

    /// <summary>The wheel preview's dial label: <paramref name="label"/>, or a sample when it is empty.</summary>
    private static string WheelPreviewLabel(string label) =>
        string.IsNullOrWhiteSpace(label) ? Loc.Tr("TouchButton_WheelSampleLabel") : label;

    /// <summary>Resolution badge shown in the canvas corner, e.g. "90 × 90 px".</summary>
    public string CanvasSizeText => $"{DeviceWidth} × {DeviceHeight} px";

    private LayerBase _selectedLayer;
    public LayerBase SelectedLayer
    {
        get => _selectedLayer;
        set
        {
            if (ReferenceEquals(_selectedLayer, value)) return;
            _selectedLayer = value;
            OnPropertyChanged(nameof(SelectedLayer));
            OnPropertyChanged(nameof(SelectedImageLayer));
            OnPropertyChanged(nameof(SelectedTextLayer));
            OnPropertyChanged(nameof(SelectedImageCommandChoice));
            OnPropertyChanged(nameof(SelectedTextCommandChoice));
            OnPropertyChanged(nameof(SelectedFontChoice));
            OnPropertyChanged(nameof(ScaleHandlesVisible));
            OnPropertyChanged(nameof(CanDeleteSelectedLayer));
            OnPropertyChanged(nameof(HasSelectedLayer));
            OnPropertyChanged(nameof(ShowStateProperties));
            UpdateSelectionBounds();
        }
    }

    public ImageLayer SelectedImageLayer => _selectedLayer as ImageLayer;
    public TextLayer SelectedTextLayer => _selectedLayer as TextLayer;

    /// <summary>Every registered command that reports text, for the wheel text layer's command picker.</summary>
    public IReadOnlyList<TextCommandChoice> TextCommandChoices => field ??= _commandRegistry.GetAll()
        .Where(c => c.IsDisplayCommand && c.GetText != null && c.Info != null)
        .Select(c => new TextCommandChoice(c.CommandName, CommandChoiceTitle(c.Info)))
        .OrderBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    /// <summary>"Group / Name" of a command, translated like every other command picker.</summary>
    private static string CommandChoiceTitle(Commands.Base.CommandInfo info)
    {
        LocalizationManager loc = LocalizationManager.Instance;
        string name = loc.TrText(info.DisplayName, info.OwnerPluginId);
        return string.IsNullOrEmpty(info.Group) ? name : $"{loc.TrText(info.Group, info.OwnerPluginId)} / {name}";
    }

    /// <summary>
    /// The command the selected text layer is bound to. Picking one writes its bare name (default
    /// parameters); parameters can be added in the command box below.
    /// </summary>
    public TextCommandChoice SelectedTextCommandChoice
    {
        get
        {
            string name = string.IsNullOrWhiteSpace(SelectedTextLayer?.TextCommand)
                ? null
                : CommandStringParser.GetName(SelectedTextLayer.TextCommand);
            return TextCommandChoices.FirstOrDefault(c => c.CommandName == name);
        }
        set
        {
            if (SelectedTextLayer == null || value == null || value == SelectedTextCommandChoice) return;
            SelectedTextLayer.TextCommand = value.CommandName;
            OnPropertyChanged();
        }
    }

    /// <summary>The fonts the selected text layer can be set to.</summary>
    public IReadOnlyList<FontChoice> FontChoices => InstalledFonts.Choices;

    /// <summary>The font of the selected text layer; the default entry clears it.</summary>
    public FontChoice SelectedFontChoice
    {
        get => SelectedTextLayer == null ? null : InstalledFonts.Find(SelectedTextLayer.FontFamily);
        set
        {
            if (SelectedTextLayer == null || value == null || value == SelectedFontChoice) return;
            SelectedTextLayer.FontFamily = value.Family;
            OnPropertyChanged();
        }
    }

    /// <summary>Every registered command that draws its own image, for the wheel layer's command picker.</summary>
    public IReadOnlyList<TextCommandChoice> ImageCommandChoices => field ??= _commandRegistry.GetAll()
        .Where(c => c.IsImageDisplayCommand && c.RenderImage != null && c.Info != null)
        .Select(c => new TextCommandChoice(c.CommandName, CommandChoiceTitle(c.Info)))
        .OrderBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    /// <summary>The image command the selected plugin layer is bound to; picking one writes its bare name.</summary>
    public TextCommandChoice SelectedImageCommandChoice
    {
        get
        {
            string command = (_selectedLayer as PluginLayer)?.Command;
            string name = string.IsNullOrWhiteSpace(command) ? null : CommandStringParser.GetName(command);
            return ImageCommandChoices.FirstOrDefault(c => c.CommandName == name);
        }
        set
        {
            if (_selectedLayer is not PluginLayer layer || value == null || value == SelectedImageCommandChoice) return;
            layer.Command = value.CommandName;
            OnPropertyChanged();
        }
    }

    /// <summary>True when a layer is selected — the right panel then shows layer properties.</summary>
    // The segments row of the all-segments list can be selected to move it, but it is no layer:
    // no properties, no deleting.
    public bool HasSelectedLayer => _selectedLayer is not null and not WheelSegmentsMarker;

    /// <summary>
    /// True when the state itself is the editing context (no layer selected) on a grid touch
    /// button — the right panel then shows the State properties instead of layer properties.
    /// </summary>
    public bool ShowStateProperties => ShowStatesSection && _selectedLayer == null;

    /// <summary>
    /// True when a deletable (user-created) layer is selected. Command-owned layers
    /// (<see cref="LayerBase.IsCommandOwned"/>) cannot be deleted manually — they are
    /// removed by unbinding the button's command — so the delete button is disabled for them.
    /// </summary>
    public bool CanDeleteSelectedLayer =>
        _selectedLayer is not null and not WheelSegmentsMarker && !_selectedLayer.IsCommandOwned;

    /// <summary>Commands of the sequence(s) that declare parameters — one card each in the
    /// Parameters tab, the same rows as the chip flyout.</summary>
    public ObservableCollection<CommandSegment> ParameterSegments { get; } = [];

    /// <summary>True when at least one command in the sequence has parameters to edit.</summary>
    public bool ShowParametersTab => ParameterSegments.Count > 0 && !IsWheelCanvas;

    /// <summary>Index of the selected right-hand tab (0 Properties, 1 Commands, 2 Parameters).</summary>
    public int RightTabIndex
    {
        get;
        set
        {
            if (!SetProperty(ref field, value)) return;

            // The scan runs the first time the tab is opened, not with every editor.
            if (value == AppsTabIndex && !_appsLoading)
            {
                _appsLoading = true;
                _ = AppList.InitializeAsync();
            }
        }
    }

    private const int ParametersTabIndex = 3;

    private void RefreshParameterSegments()
    {
        ParameterSegments.Clear();
        foreach (CommandSegment segment in CommandSlots.SelectMany(slot => slot.Commands))
        {
            if (segment is { IsKnown: true, Parameters.Count: > 0 })
                ParameterSegments.Add(segment);
        }

        OnPropertyChanged(nameof(ShowParametersTab));

        // A tab that disappears under the selection would leave the panel blank.
        if (!ShowParametersTab && RightTabIndex == ParametersTabIndex)
            RightTabIndex = 0;
    }

    private Avalonia.Media.Imaging.Bitmap _editorPreview;

    /// <summary>The rendered editor canvas: one of the <see cref="_previewBuffers"/>.</summary>
    public Avalonia.Media.Imaging.Bitmap EditorPreview
    {
        get => _editorPreview;
        private set
        {
            if (ReferenceEquals(_editorPreview, value)) return;
            _editorPreview = value;
            OnPropertyChanged(nameof(EditorPreview));
        }
    }

    // A drag renders the editor canvas on every pointer move, so the renders draw into two
    // canvas-sized buffers kept for as long as the size stays, instead of allocating a frame each
    // time. They alternate because the image control only redraws when its source changes, and so
    // a render never writes into the frame that is on screen.
    private readonly Avalonia.Media.Imaging.WriteableBitmap[] _previewBuffers = new Avalonia.Media.Imaging.WriteableBitmap[2];
    private int _previewBufferIndex;
    private bool _previewClosed;

    private void DisposePreviewBuffers()
    {
        // A render posted before the dialog closed must not allocate the buffers again.
        _previewClosed = true;
        EditorPreview = null;
        for (int i = 0; i < _previewBuffers.Length; i++)
        {
            _previewBuffers[i]?.Dispose();
            _previewBuffers[i] = null;
        }
    }

    private Avalonia.Rect _selectionBounds;

    /// <summary>
    /// On-canvas (editor-preview coordinates) bounds of the currently selected
    /// layer. Bound to the selection overlay rectangle in the XAML.
    /// </summary>
    public Avalonia.Rect SelectionBounds
    {
        get => _selectionBounds;
        private set
        {
            if (_selectionBounds == value) return;
            _selectionBounds = value;
            OnPropertyChanged(nameof(SelectionBounds));
            OnPropertyChanged(nameof(SelectionVisible));
            OnPropertyChanged(nameof(ScaleHandlesVisible));
            OnPropertyChanged(nameof(SelectionLeft));
            OnPropertyChanged(nameof(SelectionTop));
            OnPropertyChanged(nameof(SelectionWidth));
            OnPropertyChanged(nameof(SelectionHeight));
            OnPropertyChanged(nameof(HandleNwLeft));
            OnPropertyChanged(nameof(HandleNwTop));
            OnPropertyChanged(nameof(HandleNeLeft));
            OnPropertyChanged(nameof(HandleNeTop));
            OnPropertyChanged(nameof(HandleSwLeft));
            OnPropertyChanged(nameof(HandleSwTop));
            OnPropertyChanged(nameof(HandleSeLeft));
            OnPropertyChanged(nameof(HandleSeTop));
            OnPropertyChanged(nameof(HandleNLeft));
            OnPropertyChanged(nameof(HandleNTop));
            OnPropertyChanged(nameof(HandleSLeft));
            OnPropertyChanged(nameof(HandleSTop));
            OnPropertyChanged(nameof(HandleWLeft));
            OnPropertyChanged(nameof(HandleWTop));
            OnPropertyChanged(nameof(HandleELeft));
            OnPropertyChanged(nameof(HandleETop));
        }
    }

    public bool SelectionVisible => _selectedLayer != null &&
                                    _selectionBounds.Width > 0 && _selectionBounds.Height > 0;

    /// <summary>
    /// Resize handles are shown for every layer kind. Text layers use them to
    /// stretch the rendered text via Scale/ScaleY (independent of TextSize).
    /// </summary>
    public bool ScaleHandlesVisible => SelectionVisible;
    public double SelectionLeft => _selectionBounds.X;
    public double SelectionTop => _selectionBounds.Y;
    public double SelectionWidth => _selectionBounds.Width;
    public double SelectionHeight => _selectionBounds.Height;

    public const double HandleSize = 8;
    private double Hx(double cx) => cx - (HandleSize / 2.0);
    private double Hy(double cy) => cy - (HandleSize / 2.0);
    public double HandleNwLeft => Hx(SelectionLeft);
    public double HandleNwTop => Hy(SelectionTop);
    public double HandleNeLeft => Hx(SelectionLeft + SelectionWidth);
    public double HandleNeTop => Hy(SelectionTop);
    public double HandleSwLeft => Hx(SelectionLeft);
    public double HandleSwTop => Hy(SelectionTop + SelectionHeight);
    public double HandleSeLeft => Hx(SelectionLeft + SelectionWidth);
    public double HandleSeTop => Hy(SelectionTop + SelectionHeight);
    public double HandleNLeft => Hx(SelectionLeft + (SelectionWidth / 2.0));
    public double HandleNTop => Hy(SelectionTop);
    public double HandleSLeft => Hx(SelectionLeft + (SelectionWidth / 2.0));
    public double HandleSTop => Hy(SelectionTop + SelectionHeight);
    public double HandleWLeft => Hx(SelectionLeft);
    public double HandleWTop => Hy(SelectionTop + (SelectionHeight / 2.0));
    public double HandleELeft => Hx(SelectionLeft + SelectionWidth);
    public double HandleETop => Hy(SelectionTop + (SelectionHeight / 2.0));

    public ObservableCollection<MenuEntry> SystemCommandMenus { get; set; }
    public MenuEntry CurrentMenuEntry { get; set; }

    /// <summary>The card-based command picker (issue #171).</summary>
    public CommandPickerViewModel CommandPicker { get; }

    public ObservableCollection<VibrationPatternItem> VibrationPatterns => VibrationPatternCatalog.All;

    private VibrationPatternItem _selectedVibrationPattern;
    public VibrationPatternItem SelectedVibrationPattern
    {
        get => _selectedVibrationPattern;
        set
        {
            if (_selectedVibrationPattern == value) return;
            _selectedVibrationPattern = value;
            if (ButtonData != null && value != null)
                ButtonData.VibrationPattern = value.Value;
            OnPropertyChanged(nameof(SelectedVibrationPattern));
        }
    }

    // ───────── States ─────────

    // The active state at open time; restored on close so the runtime starts on the right state
    // (the editor drives ButtonData's active state to edit each selected state in place).
    private Guid _originalActiveStateId;
    private readonly IButtonStateService _buttonStates;

    /// <summary>The button's states, shown in the left-panel States list.</summary>
    public ObservableCollection<ButtonState> States => ButtonData?.States;

    /// <summary>
    /// The States section + transition UI are shown for ordinary grid touch buttons only.
    /// Side-strip canvases keep their single default state and their per-segment command flow.
    /// </summary>
    public bool ShowStatesSection => !IsStripCanvas && !IsWheelCanvas;

    /// <summary>The Button block ("Run while device is off", vibration): grid keys only. The strip
    /// canvas is never pressed as a key and a wheel canvas has neither, so no flag would be read.</summary>
    public bool ShowButtonSection => !IsStripCanvas && !IsWheelCanvas;

    /// <summary>The Commands and Apps tabs and the command-sequence strip: hidden for a wheel canvas,
    /// and for a Segmented or PluginOverride strip, which runs no user command.</summary>
    public bool ShowCommandArea => !IsWheelCanvas && (!IsStripCanvas || IsSegmentCommandMode);

    private const int CommandsTabIndex = 1;
    private const int AppsTabIndex = 2;

    /// <summary>
    /// Whether the edited state paints its background color. Off leaves the button transparent
    /// behind its layers, which is what a plugin-rendered indicator usually wants.
    /// </summary>
    public bool BackgroundEnabled
    {
        get => ButtonData?.BackgroundEnabled ?? false;
        set
        {
            if (ButtonData == null || ButtonData.BackgroundEnabled == value) return;
            ButtonData.BackgroundEnabled = value;
            OnPropertyChanged(nameof(BackgroundEnabled));
        }
    }

    /// <summary>
    /// The state currently being edited. Selecting a state makes it the button's active state so
    /// the existing preview/layers/command machinery edits it in place (the device follows live).
    /// </summary>
    public ButtonState SelectedState
    {
        get => field;
        set
        {
            if (ReferenceEquals(field, value)) return;
            field = value;
            if (value != null && ButtonData != null)
            {
                // Switching states mirrors that state's command into ButtonData.Command. That is
                // a state change, not a command assignment, so it must not reconcile.
                _switchingState = true;
                try { ButtonData.SetActiveState(value.Id); }
                finally { _switchingState = false; }
            }

            // Layers projects the active state, so the new state's layers need stamping too.
            ApplyDeviceBaseToLayers();

            SelectedLayer = null;
            OnPropertyChanged(nameof(SelectedState));
            OnPropertyChanged(nameof(BackgroundEnabled));
            OnPropertyChanged(nameof(SelectedStateLabel));
            OnPropertyChanged(nameof(BehaviorTitle));
            OnPropertyChanged(nameof(CanDeleteState));

            if (ButtonData != null)
            {
                _selectedVibrationPattern = VibrationPatterns.FirstOrDefault(p => p.Value == ButtonData.VibrationPattern);
                OnPropertyChanged(nameof(SelectedVibrationPattern));

                SeedAnimatedLayerPreviews();
                BuildCommandSlots();
                UpdateEditorPreview();
            }
        }
    }

    /// <summary>"Selected State: X" label under the States list.</summary>
    public string SelectedStateLabel => Loc.Tr("TouchButton_SelectedStateFmt", SelectedState?.Name ?? "-");

    /// <summary>"Behavior for State: X" title above the bottom command/transition area.</summary>
    public string BehaviorTitle => Loc.Tr("Common_BehaviorForStateFmt", SelectedState?.Name ?? "-");

    /// <summary>
    /// True while the assigned command owns the states: it created them, and the plugin drives
    /// which one is active. Managing them by hand would fight that, so the editor locks it —
    /// the layers inside each state stay editable.
    /// </summary>
    public bool AreStatesLocked => ButtonData?.HasCommandOwnedStates == true;

    /// <summary>Inverse of <see cref="AreStatesLocked"/>, for the views' IsEnabled bindings.</summary>
    public bool CanEditStates => !AreStatesLocked;

    /// <summary>At least two states are needed before one can be deleted.</summary>
    public bool CanDeleteState => ButtonData?.States is { Count: > 1 } && CanEditStates;

    public IRelayCommand AddStateCommand => field ??= Relay.Create(AddState);
    public IRelayCommand DuplicateStateCommand => field ??= Relay.Create(DuplicateState);
    public IRelayCommand DeleteStateCommand => field ??= Relay.Create(DeleteState);
    public IRelayCommand MoveStateUpCommand => field ??= Relay.Create(MoveStateUp);
    public IRelayCommand MoveStateDownCommand => field ??= Relay.Create(MoveStateDown);
    public IRelayCommand SetDefaultStateCommand => field ??= Relay.Create(SetDefaultStateSelected);

    private string GetUniqueStateName(string baseName)
    {
        var states = ButtonData?.States;
        if (states == null) return baseName;

        bool Exists(string name) => states.Any(s => string.Equals(s.Name, name, StringComparison.Ordinal));
        if (!Exists(baseName)) return baseName;

        var index = 1;
        while (Exists($"{baseName} {index}")) index++;
        return $"{baseName} {index}";
    }

    private void AddState()
    {
        if (ButtonData?.States == null || AreStatesLocked) return;
        var state = new ButtonState { Name = GetUniqueStateName("State") };
        ButtonData.States.Add(state);
        RefreshStateBadges();
        SelectedState = state;
        OnPropertyChanged(nameof(CanDeleteState));
    }

    // Settings used to deep-clone a state (layers are polymorphic; colors need their converter).
    private static readonly Newtonsoft.Json.JsonSerializerSettings StateCloneSettings = CreateStateCloneSettings();

    private static Newtonsoft.Json.JsonSerializerSettings CreateStateCloneSettings()
    {
        var settings = new Newtonsoft.Json.JsonSerializerSettings();
        settings.Converters.Add(new ColorJsonConverter());
        settings.Converters.Add(new LayerJsonConverter());
        return settings;
    }

    private void DuplicateState()
    {
        if (ButtonData?.States == null || SelectedState == null || AreStatesLocked) return;

        var json = Newtonsoft.Json.JsonConvert.SerializeObject(SelectedState, StateCloneSettings);
        var clone = Newtonsoft.Json.JsonConvert.DeserializeObject<ButtonState>(json, StateCloneSettings);
        if (clone == null) return;

        clone.Id = Guid.NewGuid();
        clone.IsDefault = false;
        clone.Name = GetUniqueStateName(SelectedState.Name);
        clone.RewireLayerHandlers();

        var insertAt = ButtonData.States.IndexOf(SelectedState) + 1;
        ButtonData.States.Insert(insertAt, clone);
        RefreshStateBadges();
        SelectedState = clone;
        OnPropertyChanged(nameof(CanDeleteState));
    }

    private void DeleteState()
    {
        var states = ButtonData?.States;
        if (states is not { Count: > 1 } || SelectedState == null || AreStatesLocked) return;

        var removed = SelectedState;
        var idx = states.IndexOf(removed);
        states.Remove(removed);

        // Re-point the default if it was deleted; clear dangling Specific transitions.
        if (ButtonData.DefaultStateId == removed.Id)
            ButtonData.DefaultStateId = states[0].Id;
        foreach (var s in states)
        {
            if (s.Transition?.Kind == StateTransitionKind.Specific && s.Transition.TargetStateId == removed.Id)
                s.Transition.TargetStateId = null;
        }

        RefreshStateBadges();
        var next = idx < states.Count ? states[idx] : states[^1];
        SelectedState = next;
        OnPropertyChanged(nameof(CanDeleteState));
    }

    private void MoveStateUp()
    {
        var states = ButtonData?.States;
        if (states == null || SelectedState == null || AreStatesLocked) return;
        var idx = states.IndexOf(SelectedState);
        if (idx <= 0) return;
        states.Move(idx, idx - 1);
        RefreshStateBadges();
    }

    private void MoveStateDown()
    {
        var states = ButtonData?.States;
        if (states == null || SelectedState == null || AreStatesLocked) return;
        var idx = states.IndexOf(SelectedState);
        if (idx < 0 || idx >= states.Count - 1) return;
        states.Move(idx, idx + 1);
        RefreshStateBadges();
    }

    private void SetDefaultStateSelected()
    {
        if (ButtonData == null || SelectedState == null || AreStatesLocked) return;
        ButtonData.DefaultStateId = SelectedState.Id;
        RefreshStateBadges();
    }

    /// <summary>Updates the editor-only DisplayIndex / IsDefault badges on every state.</summary>
    private void RefreshStateBadges()
    {
        var states = ButtonData?.States;
        if (states == null) return;
        for (var i = 0; i < states.Count; i++)
        {
            states[i].DisplayIndex = i + 1;
            states[i].IsDefault = states[i].Id == ButtonData.DefaultStateId;
        }
    }

    // ───────── Reset rules ─────────

    public bool ResetOnPageChange
    {
        get => ButtonData?.ResetOnPageChange ?? false;
        set
        {
            if (ButtonData == null || ButtonData.ResetOnPageChange == value) return;
            ButtonData.ResetOnPageChange = value;
            OnPropertyChanged(nameof(ResetOnPageChange));
        }
    }

    public bool ResetOnRestart
    {
        get => ButtonData?.ResetOnRestart ?? true;
        set
        {
            if (ButtonData == null || ButtonData.ResetOnRestart == value) return;
            ButtonData.ResetOnRestart = value;
            OnPropertyChanged(nameof(ResetOnRestart));
        }
    }

    // ───────── Transition ─────────
    // The transition pickers bind directly to the per-state model (SelectedState.Transition):
    // the Kind combo to .Kind, the target combo's SelectedValue to .TargetStateId. Binding to the
    // model (not a shared VM shim) means switching states never writes a value back into the wrong
    // state, and the target list stays the full, stable States collection so it never clears.

    /// <summary>The transition kinds shown in the picker (label via TransitionKindLabelConverter).</summary>
    public IReadOnlyList<StateTransitionKind> TransitionKinds { get; } =
        (StateTransitionKind[])Enum.GetValues(typeof(StateTransitionKind));

    public TouchButtonSettingsViewModel(
        ICommandBuilder commandBuilder,
        IMenuTreeBuilder menuTreeBuilder,
        ICommandRegistry commandRegistry,
        Services.Commands.ICommandStateMaterializer stateMaterializer,
        IAssetService assetService,
        IDialogService dialogService,
        ISideStripProviderRegistry sideStripRegistry,
        IDynamicTextManager dynamicTextManager,
        Services.Animation.IButtonAnimationManager buttonAnimationManager,
        Services.Animation.IAnimatedImageImporter animatedImageImporter,
        Services.Animation.IAnimatedImageCache animatedImageCache,
        Services.AppLauncher.IAppIconExtractor appIcons,
        LoupedeckConfig config,
        DeviceGeometry geometry,
        Services.Companion.ICommandLockService commandLock,
        IButtonTemplateService buttonTemplates,
        AppPickerViewModel appList,
        IButtonStateService buttonStates = null)
    {
        AppList = appList;
        _buttonStates = buttonStates;
        _commandLock = commandLock;
        _buttonTemplates = buttonTemplates;
        _commandBuilder = commandBuilder;
        _menuTreeBuilder = menuTreeBuilder;
        _commandRegistry = commandRegistry;
        _stateMaterializer = stateMaterializer;
        _assetService = assetService;
        _dialogService = dialogService;
        _sideStripRegistry = sideStripRegistry;
        _dynamicTextManager = dynamicTextManager;
        _buttonAnimationManager = buttonAnimationManager;
        _animatedImageImporter = animatedImageImporter;
        _animatedImageCache = animatedImageCache;
        _appIcons = appIcons;
        _config = config;

        // A grid touch button is edited at the device's own key size — natively, with no
        // scaling step between the editor canvas and the framebuffer. The strip canvas
        // overrides this via SetCanvasSize.
        DeviceWidth = geometry?.KeySize ?? DeviceGeometry.Default.KeySize;
        DeviceHeight = DeviceWidth;
        IsVibrationSupported = geometry?.HasVibration ?? true;

        // The provider list can change on a plugin hot-reload while the editor is open.
        _sideStripRegistry.ProvidersChanged += OnStripProvidersChanged;

        SystemCommandMenus = new ObservableCollection<MenuEntry>();
        CommandPicker = new CommandPickerViewModel(SystemCommandMenus);
    }

    public async Task InitializeAsync()
    {
        await _menuTreeBuilder.BuildInto(SystemCommandMenus, ButtonTargets.TouchButton);
    }

    /// <summary>
    /// Returns a layer name that is unique within the current button. If the
    /// base name is already taken, an incrementing suffix is appended
    /// ("Text" → "Text 1" → "Text 2" …).
    /// </summary>
    private string GetUniqueLayerName(string baseName)
        => ButtonTemplateService.UniqueLayerName(ButtonData?.Layers, baseName);

    private async Task AddImageLayer()
    {
        var path = await FileDialogHelper.OpenFileDialog();
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        var relative = _assetService.Import(path);
        if (string.IsNullOrEmpty(relative)) return;

        var layer = new ImageLayer
        {
            Name = GetUniqueLayerName(Path.GetFileNameWithoutExtension(path)),
            AssetRelativePath = relative,
            CachedImage = _assetService.Load(relative)
        };

        ApplyInitialStripHeightFit(layer, layer.CachedImage?.Width ?? 0, layer.CachedImage?.Height ?? 0);

        AddLayer(layer);
        SelectedLayer = layer;
    }

    /// <summary>
    /// Populates the static editor preview (first frame) for any animated image layers on the button.
    /// Their <see cref="ImageLayer.CachedImage"/> is runtime-only (not persisted), so a reopened
    /// button would otherwise show a blank animated layer until it plays on the device.
    /// </summary>
    private void SeedAnimatedLayerPreviews()
    {
        if (ButtonData?.Layers == null) return;

        foreach (var layer in ButtonData.Layers)
        {
            if (layer is not ImageLayer { IsAnimated: true } img) continue;
            if (img.CachedImage != null) continue;

            var anim = _animatedImageCache.Get(img.AnimatedAssetPath);
            if (anim is { Frames.Length: > 0 })
                img.CachedImage = anim.Frames[0];
        }
    }

    private async Task AddAnimatedImageLayer()
    {
        if (ButtonData == null) return;

        var path = await FileDialogHelper.OpenAnimatedImageDialog();
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        // GIF/WebP are stored as-is; a video is transcoded once here (needs ffmpeg). The decode
        // and any transcode run off the UI thread. Pass the edited surface size so a video is
        // fitted to the real target (60×270 for a side strip) preserving aspect, not a 90×90 square.
        var targetW = DeviceWidth;
        var targetH = DeviceHeight;
        // A side strip (tall, non-square) fills its height; a square button letterboxes.
        var fill = IsStripCanvas;
        var relative = await Task.Run(() => _animatedImageImporter.ImportAsync(path, targetW, targetH, fill));
        if (string.IsNullOrEmpty(relative))
        {
            // Most likely a video was picked without ffmpeg on PATH.
            return;
        }

        // Decode once via the shared cache; show the first frame as a static editor preview
        // (the editor canvas stays static — animation plays on the device).
        var anim = await Task.Run(() => _animatedImageCache.Get(relative));
        if (anim == null) return;

        var layer = new ImageLayer
        {
            Name = GetUniqueLayerName(Path.GetFileNameWithoutExtension(path)),
            AnimatedAssetPath = relative,
            // First frame as the static editor preview (the editor canvas doesn't animate; playback
            // happens on the device). Uses the notifying setter so the preview updates immediately.
            CachedImage = anim.Frames[0]
        };

        SKSizeI firstFrame = anim.Frames[0] is { } frame ? BitmapHelper.SourceSizeOf(frame) : default;
        ApplyInitialStripHeightFit(layer, firstFrame.Width, firstFrame.Height);

        AddLayer(layer);
        SelectedLayer = layer;
    }

    /// <summary>
    /// For a side-strip canvas (tall, non-square) scales a freshly added image/animation layer so it
    /// fills the strip HEIGHT while preserving aspect, centred — the surplus width is clipped by the
    /// strip, the shortfall shows a thin side margin. Nothing is squashed to the 60px width. Ordinary
    /// square touch buttons keep the default (Scale 1 = aspect-fit within the button), unaffected.
    /// </summary>
    private void ApplyInitialStripHeightFit(ImageLayer layer, int sourceWidth, int sourceHeight)
    {
        if (layer == null || !IsStripCanvas || sourceWidth <= 0 || sourceHeight <= 0) return;

        // The device render (BitmapHelper.DrawImageLayer) uses fit = min(W/sw, H/sh) then * Scale.
        // Choosing Scale = H / (sh * fit) makes the displayed height exactly the surface height,
        // so the clip fills the strip vertically at native resolution (no upscale) and the width
        // follows the aspect ratio.
        double fit = Math.Min((double)DeviceWidth / sourceWidth, (double)DeviceHeight / sourceHeight);
        if (fit <= 0) return;

        double heightFill = DeviceHeight / (sourceHeight * fit);
        if (heightFill > 1.0) layer.Scale = heightFill;
    }

    /// <summary>Adds a wheel layer bound to an image command; the analog clock is the first choice.</summary>
    private void AddLiveImageLayer()
    {
        PluginLayer layer = new()
        {
            Name = GetUniqueLayerName(Loc.Tr("TouchButton_ClockLayerName")),
            Command = "DynamicText.AnalogClock"
        };
        AddLayer(layer);
        SelectedLayer = layer;
    }

    private void AddTextLayer()
    {
        // Cap the default box to a compact, square-ish size so a tall surface (e.g. the
        // 60×270 side strip) doesn't get a text box spanning the whole panel. A normal
        // 90×90 button is unaffected (min(90,90) = 90).
        var box = Math.Min(DeviceWidth, DeviceHeight);
        var layer = new TextLayer
        {
            FontFamily = TextFontSettings.DefaultFamily,
            Name = GetUniqueLayerName("Text"),
            Text = "Text",
            BoxWidth = box,
            BoxHeight = box
        };
        AddLayer(layer);
        SelectedLayer = layer;
    }

    /// <summary>
    /// Arranges the icon and caption of the active state as <paramref name="template"/>. Layers the
    /// template drops are listed to the user first, because they may have drawn them by hand.
    /// </summary>
    private async Task ApplyTemplate(ButtonTemplate template)
    {
        if (ButtonData == null || IsStripCanvas) return;

        ButtonTemplateOutcome outcome = await _buttonTemplates.ApplyAsync(ButtonData, template, DeviceWidth,
            DeviceHeight);
        if (!outcome.Applied)
            return;

        // A caption the template created has not been stamped with the surface size yet.
        ApplyDeviceBaseToLayers();

        if (_selectedLayer != null && !ButtonData.Layers.Contains(_selectedLayer))
            SelectedLayer = null;

        if (outcome.AddedIcon != null)
            SelectedLayer = outcome.AddedIcon;

        UpdateEditorPreview();
        UpdateSelectionBounds();
    }

    private async Task AddSymbolLayer()
    {
        if (ButtonData == null) return;

        SymbolLayer layer = await _buttonTemplates.PickSymbolLayerAsync(ButtonData.Layers);
        if (layer == null) return;

        AddLayer(layer);
        SelectedLayer = layer;
    }

    /// <summary>
    /// Re-opens the symbol picker for the currently selected <see cref="SymbolLayer"/>
    /// and applies the new symbol. Invoked from the properties panel.
    /// </summary>
    public async Task ChangeSelectedSymbolAsync()
    {
        if (_selectedLayer is not SymbolLayer symbol) return;

        var request = new SymbolPickerRequest
        {
            CurrentSymbolId = symbol.IsImageIcon ? null : symbol.SymbolId,
            CurrentPackIconKey = symbol.IsImageIcon ? symbol.IconSource : null
        };
        var result = await _dialogService.ShowDialogAsync<SymbolPickerViewModel, DialogResult>(
            vm => vm.Initialize(request));

        if (result is not { IsConfirmed: true }) return;

        // Keep the box's larger side and re-fit it to the new icon's aspect ratio.
        double size = Math.Max(symbol.EffectiveScaleX, symbol.EffectiveScaleY);

        if (request.SelectedPackIcon is { } icon)
        {
            if (_buttonTemplates.ApplyPackIcon(symbol, icon, size))
                symbol.Name = icon.DisplayName;
            return;
        }

        if (request.SelectedSymbol is not { } def) return;

        // A glyph replaces a pack icon entirely.
        symbol.IconAssetPath = null;
        symbol.IconSource = null;
        symbol.KeepOriginalColors = false;
        symbol.SymbolId = def.Id;
        symbol.Name = def.DisplayName;
        symbol.FitScaleToGlyph(size);
    }

    /// <summary>
    /// The installed applications listed in the Apps tab: the same list the application picker shows,
    /// scanned the first time the tab is opened.
    /// </summary>
    public AppPickerViewModel AppList { get; }

    private bool _appsLoading;

    /// <summary>
    /// Adds the launch command of <paramref name="app"/> to <paramref name="slot"/> (the active
    /// sequence when null) at <paramref name="index"/> (the end when negative), the way a command from
    /// the Commands tab is added.
    /// </summary>
    /// <remarks>
    /// The application's icon comes along when the sequence was empty: on a grid key only while the
    /// key has no layers yet, so hand-made artwork is never covered; on a FreeDraw strip as a layer
    /// centred in that sequence's tap zone, so each of the three zones can show its own application.
    /// Nothing is replaced and nothing is asked.
    /// </remarks>
    public async Task InsertApplicationAsync(Services.AppLauncher.InstalledApp app, CommandSequenceSlot slot = null,
        int index = -1)
    {
        slot ??= ActiveSlot;
        if (app == null || slot == null || ButtonData == null) return;

        string command = Services.AppLauncher.AppAssignment.BuildLaunchCommand(app);
        bool wasEmpty = slot.Commands.Count == 0;
        if (!slot.InsertRawAt(command, index < 0 ? slot.Commands.Count : index)) return;

        int segment = IsSegmentCommandMode ? CommandSlots.IndexOf(slot) : -1;
        bool addIcon = wasEmpty && CanEditCanvas && (segment >= 0 || ButtonData.Layers.Count == 0);
        if (!addIcon) return;

        // Resolved before the button is touched: extraction is file IO and P/Invoke.
        string relative = null;
        try
        {
            string iconFile = await _appIcons.GetIconFileAsync(app);
            if (!string.IsNullOrEmpty(iconFile) && File.Exists(iconFile))
                relative = _assetService.Import(iconFile);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ButtonEditor] Icon for '{app.Name}' could not be imported: {ex.Message}");
        }

        if (string.IsNullOrEmpty(relative)) return;

        // The surface the icon is fitted to: a grid key, or one tap zone of the strip.
        int zoneHeight = segment >= 0 ? DeviceHeight / RotaryButtonPage.StripSegmentCount : DeviceHeight;
        int keySize = Math.Min(DeviceWidth, zoneHeight);

        ImageLayer layer = Services.AppLauncher.AppAssignment.AddIconLayer(
            ButtonData, app, relative, replaceLayers: false, GetUniqueLayerName(app.Name), keySize);
        if (layer == null) return;

        // Stamped like every layer the editor adds, so the size fields report real device pixels
        // instead of the 90px key default.
        layer.DeviceBaseWidth = DeviceWidth;
        layer.DeviceBaseHeight = DeviceHeight;

        // In a tap zone the icon gets the strip size, fitted against the canvas's short edge the way
        // the renderer resolves it. Layer positions are offsets from the canvas centre; this one is
        // the zone's centre.
        if (segment >= 0)
        {
            layer.Scale = Services.AppLauncher.AppAssignment.IconScaleFor(
                Services.AppLauncher.AppAssignment.StripIconSizePx, Math.Min(DeviceWidth, DeviceHeight));
            layer.PositionY = (int)Math.Round(((segment + 0.5) * zoneHeight) - (DeviceHeight / 2.0));
        }

        layer.CachedImage = _assetService.Load(relative);
        SelectedLayer = layer;
        UpdateEditorPreview();
    }

    private void RemoveSelectedLayer()
    {
        // A layer owned by the command that is still bound belongs to that command: it is removed
        // by unbinding it (the dynamic-text manager's orphan sweep), not by hand. One left behind
        // by a command that is gone has no owner left, so the user may delete it.
        if (_selectedLayer == null || IsOwnedByBoundCommand(_selectedLayer)) return;
        var idx = ButtonData.Layers.IndexOf(_selectedLayer);
        if (idx < 0) return;
        ButtonData.Layers.Remove(_selectedLayer);
        // Prefer the item that moved into the freed slot (the one below); fall back
        // to the new last item (the one above) when the removed layer was last.
        var next = (idx < ButtonData.Layers.Count) ? ButtonData.Layers[idx]
            : (ButtonData.Layers.Count > 0 ? ButtonData.Layers[^1] : null);
        ReselectAfterMove(next);
    }

    /// <summary>True when the layer belongs to the command currently bound to the button.</summary>
    private bool IsOwnedByBoundCommand(LayerBase layer) => layer?.IsOwnedBy(ButtonData?.Command) == true;

    /// <summary>
    /// The layers as the list shows them: topmost first. A reversed mirror of
    /// <see cref="TouchButton.Layers"/>, whose order is the paint order (first at the bottom); on a
    /// split wheel's all-segments surface grouped around a row for the segments
    /// (<see cref="SharedSurfaceLayerView"/>).
    /// </summary>
    public ObservableCollection<LayerBase> LayerListItems
    {
        get;
        private set
        {
            if (ReferenceEquals(field, value)) return;
            field = value;
            OnPropertyChanged(nameof(LayerListItems));
        }
    }

    private void RebuildLayerListItems()
    {
        (LayerListItems as IDisposable)?.Dispose();
        if (ButtonData?.Layers == null)
            LayerListItems = null;
        else if (IsEditingSharedSurface)
            LayerListItems = new SharedSurfaceLayerView(ButtonData.Layers, _segmentsMarker);
        else
            LayerListItems = new ReversedLayerView(ButtonData.Layers);
    }

    /// <summary>
    /// Puts the all-segments surface's layers behind the segments first, each side in its paint
    /// order. Changes nothing that is drawn; it keeps the canvas in the order the list shows.
    /// </summary>
    private void SortSharedLayers()
    {
        var layers = ButtonData.Layers;
        var ordered = layers.Where(l => l.BehindSegments).Concat(layers.Where(l => !l.BehindSegments)).ToList();
        for (int k = 0; k < ordered.Count; k++)
        {
            int at = layers.IndexOf(ordered[k]);
            if (at != k) layers.Move(at, k);
        }
    }

    /// <summary>
    /// ▲ / ▼ on the all-segments surface: a step within the layer's side, or across the segments
    /// row onto the other side when the layer is the one next to it. The canvas keeps the order
    /// the list shows: the layers behind the segments first, those in front after them.
    /// </summary>
    private void MoveSharedLayer(LayerBase layer, bool up)
    {
        var layers = ButtonData.Layers;
        SortSharedLayers();

        int boundary = layers.Count(l => l.BehindSegments);

        // The segments row moves by passing the layer next to it to the other side: up takes the
        // lowest layer in front of the segments behind them, down brings the topmost layer
        // behind them to the front.
        if (layer is WheelSegmentsMarker)
        {
            if (up && boundary < layers.Count)
                layers[boundary].BehindSegments = true;
            else if (!up && boundary > 0)
                layers[boundary - 1].BehindSegments = false;
            ReselectAfterMove(layer);
            return;
        }

        int i = layers.IndexOf(layer);
        if (i < 0) return;

        if (up)
        {
            if (layer.BehindSegments && i == boundary - 1)
                layer.BehindSegments = false;
            else if (i < layers.Count - 1)
                layers.Move(i, i + 1);
        }
        else
        {
            if (!layer.BehindSegments && i == boundary)
                layer.BehindSegments = true;
            else if (i > 0)
                layers.Move(i, i - 1);
        }

        ReselectAfterMove(layer);
    }

    /// <summary>Moves the selected layer one step up the list, i.e. one step closer to the top of the stack.</summary>
    private void MoveSelectedLayerUp()
    {
        if (_selectedLayer == null) return;
        if (IsEditingSharedSurface)
        {
            MoveSharedLayer(_selectedLayer, up: true);
            return;
        }

        var layer = _selectedLayer;
        var idx = ButtonData.Layers.IndexOf(layer);
        if (idx < 0 || idx >= ButtonData.Layers.Count - 1) return;
        ButtonData.Layers.Move(idx, idx + 1);
        ReselectAfterMove(layer);
    }

    /// <summary>Moves the selected layer one step down the list, i.e. further behind the others.</summary>
    private void MoveSelectedLayerDown()
    {
        if (_selectedLayer == null) return;
        if (IsEditingSharedSurface)
        {
            MoveSharedLayer(_selectedLayer, up: false);
            return;
        }

        var layer = _selectedLayer;
        var idx = ButtonData.Layers.IndexOf(layer);
        if (idx <= 0) return;
        ButtonData.Layers.Move(idx, idx - 1);
        ReselectAfterMove(layer);
    }

    /// <summary>
    /// Re-applies the selection after an <see cref="ObservableCollection{T}.Move"/>.
    /// The ListBox processes the move (remove+add) on a later dispatcher cycle and
    /// clears its selection in the process, so a synchronous re-assign gets
    /// overwritten — posting it ensures it lands after the ListBox has caught up.
    /// </summary>
    private void ReselectAfterMove(LayerBase layer)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            // Force the property to re-raise even if the value matches, so the
            // ListBox is told to re-select after it cleared its own selection.
            SelectedLayer = null;
            SelectedLayer = layer;
        });
    }

    /// <summary>
    /// Resets the touch button to a blank default state — clears command, text, image and
    /// all visual settings. Triggers a single redraw at the end via Refresh().
    /// </summary>
    public void ClearButton()
    {
        if (ButtonData == null) return;

        // For a free-draw strip, also clear all three per-segment commands (the strip's
        // "command" is the three segments, not ButtonData.Command).
        if (IsSegmentCommandMode && _stripPage != null)
        {
            for (var i = 0; i < RotaryButtonPage.StripSegmentCount; i++)
                _stripPage.SetStripSegmentCommand(i, null);
        }

        var b = ButtonData;
        var fresh = new ButtonState { Name = "Default" };
        b.IgnoreRefresh = true;
        try
        {
            // Reset to a single blank default state, discarding any extra states, and reset the
            // mode / reset rules. (A single state behaves like a non-stateful button.)
            b.States.Clear();
            b.States.Add(fresh);
            b.DefaultStateId = fresh.Id;
            b.StateOwnerCommand = null;
            b.Mode = ButtonStateMode.Local;
            b.ResetOnPageChange = false;
            b.ResetOnRestart = true;
            b.SetActiveState(fresh.Id);

            // A wheel canvas resets to the automatic layout's layers rather than to nothing, so
            // "Reset" means "back to the stock look", not a blank disc.
            if (IsWheelCanvas && EditedSegment != null)
                WheelCanvasDefaults.PopulateSegment(b);
            else if (IsEditingSharedSurface)
                WheelCanvasDefaults.PopulateSplitShared(b);
            else if (IsWheelCanvas)
                WheelCanvasDefaults.Populate(b, DeviceWidth);
        }
        finally
        {
            b.IgnoreRefresh = false;
        }
        b.Refresh();

        RefreshStateBadges();
        SelectedLayer = null;
        // Selecting the fresh state re-points the preview, layers and command slots.
        SelectedState = fresh;

        OnPropertyChanged(nameof(AreStatesLocked));
        OnPropertyChanged(nameof(CanEditStates));
        OnPropertyChanged(nameof(CanDeleteState));
        OnPropertyChanged(nameof(ResetOnPageChange));
        OnPropertyChanged(nameof(ResetOnRestart));
    }

    /// <summary>True while the editor switches the edited state, see <see cref="SelectedState"/>.</summary>
    private bool _switchingState;

    private void ButtonData_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Switching the active state swaps the whole layer collection the button projects.
        if (e.PropertyName == nameof(TouchButton.Layers))
            RebuildLayerListItems();

        if (e.PropertyName == nameof(TouchButton.Command))
        {
            // Every insert/clear/parameter edit funnels through ButtonData.Command, so this is the
            // one place a command that declares its own states needs to be reconciled.
            ReconcileCommandStates();

            // Re-scan dynamic-text/-image commands and animated display commands so a display
            // command's layer appears (or its orphaned layer disappears) immediately while the
            // editor is open, instead of only after it closes. The strip-canvas surface is not a
            // real page button, so skip it.
            if (!IsStripCanvas && !IsWheelCanvas)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _dynamicTextManager.Rescan();
                    _buttonAnimationManager.Rescan();
                });
            }
        }
    }

    /// <summary>
    /// Creates the states of a newly assigned command that declares its own, or releases them
    /// again when that command was removed or replaced.
    /// </summary>
    private void ReconcileCommandStates()
    {
        if (ButtonData == null || IsStripCanvas || IsWheelCanvas || _switchingState) return;

        Services.Commands.StateSyncResult result = _stateMaterializer.Reconcile(ButtonData);
        switch (result)
        {
            case Services.Commands.StateSyncResult.Unchanged:
                return;

            case Services.Commands.StateSyncResult.ReleaseRequested:
                EventHandler<StateReleaseRequest> handler = StateReleaseRequested;
                if (handler == null)
                {
                    // No view attached to ask: keep the states, the non-destructive choice.
                    CompleteStateRelease(keepStates: true);
                    return;
                }

                handler(this, new StateReleaseRequest(_stateMaterializer.GetOwnerDisplayName(ButtonData)));
                return;

            default:
                RefreshAfterStateSync();
                return;
        }
    }

    /// <summary>
    /// Raised when the command owning the button's states was removed or replaced. The view asks
    /// the user and answers through <see cref="CompleteStateRelease"/>.
    /// </summary>
    public event EventHandler<StateReleaseRequest> StateReleaseRequested;

    /// <summary>
    /// Applies the user's answer to a <see cref="StateReleaseRequested"/> prompt, then lets a
    /// replacement command that declares states create its own.
    /// </summary>
    public void CompleteStateRelease(bool keepStates)
    {
        if (ButtonData == null) return;

        _stateMaterializer.Release(ButtonData, keepStates);
        _stateMaterializer.Reconcile(ButtonData);
        RefreshAfterStateSync();
    }

    /// <summary>Re-reads everything the state set feeds after it was created or released.</summary>
    private void RefreshAfterStateSync()
    {
        RefreshStateBadges();
        SelectedLayer = null;
        SelectedState = ButtonData?.States.Count > 0 ? ButtonData.States[0] : null;

        OnPropertyChanged(nameof(AreStatesLocked));
        OnPropertyChanged(nameof(CanEditStates));
        OnPropertyChanged(nameof(CanDeleteState));
        OnPropertyChanged(nameof(BackgroundEnabled));
        OnPropertyChanged(nameof(ResetOnPageChange));
        OnPropertyChanged(nameof(ResetOnRestart));
    }

    // Set while an ItemChanged refresh is queued, so a burst of notifications from one edit
    // renders the editor canvas once instead of once per notification.
    private int _itemChangedRefreshPending;

    private void ButtonData_ItemChanged(object sender, EventArgs e)
    {
        HasEdits = true;

        if (Interlocked.Exchange(ref _itemChangedRefreshPending, 1) == 1) return;

        // ItemChanged may fire on a background thread (e.g. dynamic-text timer).
        // Dispatch to the UI thread so the bitmap swap and property notifications
        // are observed by Avalonia bindings.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Volatile.Write(ref _itemChangedRefreshPending, 0);
            UpdateEditorPreview();
            UpdateSelectionBounds();
            // A command typed into the box, rather than picked, must move the pickers along.
            OnPropertyChanged(nameof(SelectedTextCommandChoice));
            OnPropertyChanged(nameof(SelectedImageCommandChoice));
            OnPropertyChanged(nameof(SelectedFontChoice));
        });
    }

    // A command-drawn image changed (e.g. the analog clock ticked): repaint the preview only.
    private void ButtonData_LiveContentChanged(object sender, EventArgs e) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(UpdateEditorPreview);

    private void UpdateEditorPreview()
    {
        if (ButtonData == null || _config == null) return;
        // Segment dividers only make sense (and are only drawn) for a free-draw side
        // strip; the renderer further gates them on the grid toggle.
        var segmentCount = IsSegmentCommandMode ? RotaryButtonPage.StripSegmentCount : 0;
        int width = EditorCanvasWidth;
        int height = EditorCanvasHeight;
        if (_previewClosed || width <= 0 || height <= 0) return;

        _previewBufferIndex ^= 1;
        Avalonia.Media.Imaging.WriteableBitmap target = _previewBuffers[_previewBufferIndex];
        if (target == null || target.PixelSize.Width != width || target.PixelSize.Height != height)
        {
            target?.Dispose();
            target = new Avalonia.Media.Imaging.WriteableBitmap(new Avalonia.PixelSize(width, height),
                new Avalonia.Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);
            _previewBuffers[_previewBufferIndex] = target;
        }

        using (Avalonia.Platform.ILockedFramebuffer frame = target.Lock())
        {
            SKImageInfo info = new(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using SKSurface surface = SKSurface.Create(info, frame.Address, frame.RowBytes);
            BitmapHelper.DrawEditorCanvas(surface.Canvas, ButtonData, width, height, DeviceWidth, DeviceHeight,
                ShowGrid, GridStepDevice, segmentCount, PreviewDial, roundMask: IsWheelCanvas,
                wheelSplit: IsWheelSplit ? _wheel.SplitMode : Models.WheelSplitMode.None,
                wheelSegment: IsEditingSharedSurface ? -1 : _wheelSegmentIndex,
                wheelSegments: IsWheelSplit ? _wheel.ActiveSegments().Select(s => s.Canvas).ToArray() : null,
                wheelShared: IsWheelSplit ? _wheel.SplitCanvas : null,
                wheelHover: _hoverSegment,
                wheelDividers: _wheel?.ShowSplitDividers ?? true);
        }

        EditorPreview = target;
    }

    /// <summary>
    /// Re-renders the editor preview + selection overlay without going through
    /// the TouchButton.ItemChanged pipeline. Called from the code-behind during
    /// drag while <see cref="TouchButton.IgnoreRefresh"/> is true so the device
    /// is not flooded with serial writes.
    /// </summary>
    public void PreviewRefreshDuringDrag()
    {
        UpdateEditorPreview();
        UpdateSelectionBounds();
    }

    private void UpdateSelectionBounds()
    {
        if (_selectedLayer == null)
        {
            SelectionBounds = default;
            return;
        }

        var rect = BitmapHelper.GetLayerEditorBounds(
            _selectedLayer, EditorCanvasWidth, EditorCanvasHeight, DeviceWidth, DeviceHeight);

        if (rect == null)
        {
            SelectionBounds = default;
            return;
        }

        var r = rect.Value;
        SelectionBounds = new Avalonia.Rect(r.Left, r.Top, r.Width, r.Height);
    }

    /// <summary>
    /// Detaches event handlers — called by the View when the dialog closes so the
    /// (singleton) TouchButton does not keep the (transient) ViewModel alive.
    /// </summary>
    public void Cleanup()
    {
        if (ButtonData != null)
        {
            // Restore the runtime active state so editor state-switching is not persisted as the
            // live state (the device repaints to it on close).
            if (ButtonData.States?.Any(s => s.Id == _originalActiveStateId) == true)
                ButtonData.SetActiveState(_originalActiveStateId);
            else
                ButtonData.ResetToDefaultState();

            // The live state may have moved on while the editor was open (or the command changed),
            // so a command that reports per-button states is asked again.
            _buttonStates?.RefreshState(ButtonData);

            ButtonData.ItemChanged -= ButtonData_ItemChanged;
            ButtonData.PropertyChanged -= ButtonData_PropertyChanged;
            ButtonData.LiveContentChanged -= ButtonData_LiveContentChanged;
        }

        (LayerListItems as IDisposable)?.Dispose();
        LayerListItems = null;

        DisposePreviewBuffers();

        _sideStripRegistry.ProvidersChanged -= OnStripProvidersChanged;

        foreach (var slot in CommandSlots)
            slot.Cleanup();

        CommandPicker.Cleanup();
        AppList.StopLoading();
    }

    private void OnStripProvidersChanged()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(AvailableStripProviders));
            OnPropertyChanged(nameof(SelectedStripProvider));
        });
    }
}

/// <summary>A dynamic-text command a wheel text layer can take its text from.</summary>
public sealed record TextCommandChoice(string CommandName, string Title);
