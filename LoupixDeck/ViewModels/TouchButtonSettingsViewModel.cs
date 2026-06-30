using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Models;
using LoupixDeck.Models.Converter;
using LoupixDeck.Models.Layers;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services;
using LoupixDeck.Services.Commands;
using LoupixDeck.Services.Plugins;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;
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
        }

        ButtonData = parameter;

        if (ButtonData != null)
        {
            ButtonData.ItemChanged += ButtonData_ItemChanged;
            ButtonData.PropertyChanged += ButtonData_PropertyChanged;

            // Remember the runtime active state and start editing it; restored on Cleanup so the
            // editor's state-switching does not leave the button on a non-default state at runtime.
            _originalActiveStateId = ButtonData.ActiveStateId;
            RefreshStateBadges();
            SelectedState = ButtonData.ActiveState;

            SeedAnimatedLayerPreviews();
            UpdateEditorPreview();

            _selectedVibrationPattern = VibrationPatterns.FirstOrDefault(
                p => p.Value == ButtonData.VibrationPattern);
            OnPropertyChanged(nameof(SelectedVibrationPattern));
        }

        BuildCommandSlots();

        OnPropertyChanged(nameof(States));
        OnPropertyChanged(nameof(ResetOnPageChange));
        OnPropertyChanged(nameof(ResetOnRestart));
        OnPropertyChanged(nameof(ButtonNumber));
        OnPropertyChanged(nameof(ButtonLabel));
    }

    private readonly ICommandBuilder _commandBuilder;
    private readonly IMenuTreeBuilder _menuTreeBuilder;
    private readonly ICommandRegistry _commandRegistry;
    private readonly IAssetService _assetService;
    private readonly IDialogService _dialogService;
    private readonly ISideStripProviderRegistry _sideStripRegistry;
    private readonly IDynamicTextManager _dynamicTextManager;
    private readonly Services.Animation.IAnimatedImageImporter _animatedImageImporter;
    private readonly Services.Animation.IAnimatedImageCache _animatedImageCache;
    private readonly LoupedeckConfig _config;

    public const int EditorCanvasSize = 600;

    // Device-pixel dimensions of the edited surface. 90×90 for grid touch buttons;
    // set to 60×270 for a Razer side-strip free-draw canvas via SetCanvasSize.
    public int DeviceWidth { get; private set; } = 90;
    public int DeviceHeight { get; private set; } = 90;

    /// <summary>Editor → device coordinate factor (canvas pixels per device pixel).</summary>
    public double EditorToDeviceScale => BitmapHelper.ComputeEditorFrame(DeviceWidth, DeviceHeight).Scale;

    /// <summary>Rendered frame size (canvas px), aspect-correct for the device surface.</summary>
    public double FrameWidth => BitmapHelper.ComputeEditorFrame(DeviceWidth, DeviceHeight).FrameWidth;
    public double FrameHeight => BitmapHelper.ComputeEditorFrame(DeviceWidth, DeviceHeight).FrameHeight;

    /// <summary>Top-left of the centered frame inside the square editor canvas.</summary>
    public double FrameOffsetX => (EditorCanvasSize - FrameWidth) / 2.0;
    public double FrameOffsetY => (EditorCanvasSize - FrameHeight) / 2.0;

    /// <summary>
    /// Sets the edited surface's device-pixel dimensions (e.g. 60×270 for a side-strip
    /// free-draw canvas). Call before <see cref="Initialize"/>. Defaults to 90×90.
    /// </summary>
    public void SetCanvasSize(int deviceWidth, int deviceHeight)
    {
        DeviceWidth = Math.Max(1, deviceWidth);
        DeviceHeight = Math.Max(1, deviceHeight);
        OnPropertyChanged(nameof(DeviceWidth));
        OnPropertyChanged(nameof(DeviceHeight));
        OnPropertyChanged(nameof(EditorToDeviceScale));
        OnPropertyChanged(nameof(FrameWidth));
        OnPropertyChanged(nameof(FrameHeight));
        OnPropertyChanged(nameof(FrameOffsetX));
        OnPropertyChanged(nameof(FrameOffsetY));
        OnPropertyChanged(nameof(CanvasSizeText));
        UpdateEditorPreview();
        UpdateSelectionBounds();
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
        _viewport = new Avalonia.Size(width, height);
    }

    private void FitToViewport()
    {
        if (_viewport.Width <= 0 || _viewport.Height <= 0) return;
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

    private static readonly string[] SegmentTitles = ["Top segment", "Middle segment", "Bottom segment"];

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

    /// <summary>(Re)builds the command sequence slots for the current mode and selects the first.</summary>
    private void BuildCommandSlots()
    {
        foreach (var slot in CommandSlots)
            slot.Cleanup();
        CommandSlots.Clear();
        ActiveSlot = null;

        if (ButtonData == null) return;

        if (IsSegmentCommandMode && _stripPage != null)
        {
            for (var i = 0; i < RotaryButtonPage.StripSegmentCount; i++)
            {
                var index = i;
                CommandSlots.Add(new CommandSequenceSlot(
                    SegmentTitles[index], _commandBuilder, _commandRegistry,
                    () => _stripPage.GetStripSegmentCommand(index),
                    v => _stripPage.SetStripSegmentCommand(index, string.IsNullOrWhiteSpace(v) ? null : v)));
            }
        }
        else
        {
            CommandSlots.Add(new CommandSequenceSlot(
                "Command sequence", _commandBuilder, _commandRegistry,
                () => ButtonData.Command,
                v => ButtonData.Command = string.IsNullOrWhiteSpace(v) ? null : v));
        }

        if (CommandSlots.Count > 0)
            SetActiveSlot(CommandSlots[0]);
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
    public IAsyncRelayCommand  AddSymbolLayerCommand => field ??= Relay.Create(AddSymbolLayer);
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

    /// <summary>Window title, e.g. "Touch Button 1".</summary>
    public string ButtonLabel => $"Touch Button {ButtonNumber}";

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
            OnPropertyChanged(nameof(ScaleHandlesVisible));
            OnPropertyChanged(nameof(CanDeleteSelectedLayer));
            OnPropertyChanged(nameof(HasSelectedLayer));
            OnPropertyChanged(nameof(ShowStateProperties));
            UpdateSelectionBounds();
        }
    }

    public ImageLayer SelectedImageLayer => _selectedLayer as ImageLayer;
    public TextLayer SelectedTextLayer => _selectedLayer as TextLayer;

    /// <summary>True when a layer is selected — the right panel then shows layer properties.</summary>
    public bool HasSelectedLayer => _selectedLayer != null;

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
    public bool CanDeleteSelectedLayer => _selectedLayer != null && !_selectedLayer.IsCommandOwned;

    private SKBitmap _editorPreview;

    public SKBitmap EditorPreview
    {
        get => _editorPreview;
        private set
        {
            if (ReferenceEquals(_editorPreview, value)) return;
            _editorPreview = value;
            OnPropertyChanged(nameof(EditorPreview));
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
    public double HandleNwTop  => Hy(SelectionTop);
    public double HandleNeLeft => Hx(SelectionLeft + SelectionWidth);
    public double HandleNeTop  => Hy(SelectionTop);
    public double HandleSwLeft => Hx(SelectionLeft);
    public double HandleSwTop  => Hy(SelectionTop + SelectionHeight);
    public double HandleSeLeft => Hx(SelectionLeft + SelectionWidth);
    public double HandleSeTop  => Hy(SelectionTop + SelectionHeight);
    public double HandleNLeft  => Hx(SelectionLeft + (SelectionWidth / 2.0));
    public double HandleNTop   => Hy(SelectionTop);
    public double HandleSLeft  => Hx(SelectionLeft + (SelectionWidth / 2.0));
    public double HandleSTop   => Hy(SelectionTop + SelectionHeight);
    public double HandleWLeft  => Hx(SelectionLeft);
    public double HandleWTop   => Hy(SelectionTop + (SelectionHeight / 2.0));
    public double HandleELeft  => Hx(SelectionLeft + SelectionWidth);
    public double HandleETop   => Hy(SelectionTop + (SelectionHeight / 2.0));

    public ObservableCollection<MenuEntry> SystemCommandMenus { get; set; }
    public MenuEntry CurrentMenuEntry { get; set; }

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

    /// <summary>The button's states, shown in the left-panel States list.</summary>
    public ObservableCollection<ButtonState> States => ButtonData?.States;

    /// <summary>
    /// The States section + transition UI are shown for ordinary grid touch buttons only.
    /// Side-strip canvases keep their single default state and their per-segment command flow.
    /// </summary>
    public bool ShowStatesSection => !IsStripCanvas;

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
                ButtonData.SetActiveState(value.Id);

            SelectedLayer = null;
            OnPropertyChanged(nameof(SelectedState));
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
    public string SelectedStateLabel => $"Selected State: {SelectedState?.Name ?? "-"}";

    /// <summary>"Behavior for State: X" title above the bottom command/transition area.</summary>
    public string BehaviorTitle => $"Behavior for State: {SelectedState?.Name ?? "-"}";

    /// <summary>At least two states are needed before one can be deleted.</summary>
    public bool CanDeleteState => ButtonData?.States is { Count: > 1 };

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
        if (ButtonData?.States == null) return;
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
        if (ButtonData?.States == null || SelectedState == null) return;

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
        if (states is not { Count: > 1 } || SelectedState == null) return;

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
        if (states == null || SelectedState == null) return;
        var idx = states.IndexOf(SelectedState);
        if (idx <= 0) return;
        states.Move(idx, idx - 1);
        RefreshStateBadges();
    }

    private void MoveStateDown()
    {
        var states = ButtonData?.States;
        if (states == null || SelectedState == null) return;
        var idx = states.IndexOf(SelectedState);
        if (idx < 0 || idx >= states.Count - 1) return;
        states.Move(idx, idx + 1);
        RefreshStateBadges();
    }

    private void SetDefaultStateSelected()
    {
        if (ButtonData == null || SelectedState == null) return;
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
        IAssetService assetService,
        IDialogService dialogService,
        ISideStripProviderRegistry sideStripRegistry,
        IDynamicTextManager dynamicTextManager,
        Services.Animation.IAnimatedImageImporter animatedImageImporter,
        Services.Animation.IAnimatedImageCache animatedImageCache,
        LoupedeckConfig config)
    {
        _commandBuilder = commandBuilder;
        _menuTreeBuilder = menuTreeBuilder;
        _commandRegistry = commandRegistry;
        _assetService = assetService;
        _dialogService = dialogService;
        _sideStripRegistry = sideStripRegistry;
        _dynamicTextManager = dynamicTextManager;
        _animatedImageImporter = animatedImageImporter;
        _animatedImageCache = animatedImageCache;
        _config = config;

        // The provider list can change on a plugin hot-reload while the editor is open.
        _sideStripRegistry.ProvidersChanged += OnStripProvidersChanged;

        SystemCommandMenus = new ObservableCollection<MenuEntry>();
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
    {
        if (ButtonData?.Layers == null)
            return baseName;

        bool Exists(string name) =>
            ButtonData.Layers.Any(l => string.Equals(l.Name, name, StringComparison.Ordinal));

        if (!Exists(baseName))
            return baseName;

        var index = 1;
        while (Exists($"{baseName} {index}"))
            index++;

        return $"{baseName} {index}";
    }

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

        ButtonData.Layers.Add(layer);
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
        // and any transcode run off the UI thread.
        var relative = await Task.Run(() => _animatedImageImporter.ImportAsync(path));
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

        ButtonData.Layers.Add(layer);
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
            Name = GetUniqueLayerName("Text"),
            Text = "Text",
            BoxWidth = box,
            BoxHeight = box
        };
        ButtonData.Layers.Add(layer);
        SelectedLayer = layer;
    }

    private async Task AddSymbolLayer()
    {
        if (ButtonData == null) return;

        var request = new SymbolPickerRequest();
        var result = await _dialogService.ShowDialogAsync<SymbolPickerViewModel, DialogResult>(
            vm => vm.Initialize(request));

        if (result is not { IsConfirmed: true } || request.SelectedSymbol == null) return;

        var def = request.SelectedSymbol;
        SymbolLayer layer = new()
        {
            Name = GetUniqueLayerName(def.DisplayName),
            SymbolId = def.Id,
            Scale = 0.7
        };
        ButtonData.Layers.Add(layer);
        SelectedLayer = layer;
    }

    /// <summary>
    /// Re-opens the symbol picker for the currently selected <see cref="SymbolLayer"/>
    /// and applies the new symbol. Invoked from the properties panel.
    /// </summary>
    public async Task ChangeSelectedSymbolAsync()
    {
        if (_selectedLayer is not SymbolLayer symbol) return;

        var request = new SymbolPickerRequest { CurrentSymbolId = symbol.SymbolId };
        var result = await _dialogService.ShowDialogAsync<SymbolPickerViewModel, DialogResult>(
            vm => vm.Initialize(request));

        if (result is not { IsConfirmed: true } || request.SelectedSymbol == null) return;

        var def = request.SelectedSymbol;
        symbol.SymbolId = def.Id;
        symbol.Name = def.DisplayName;
    }

    private void RemoveSelectedLayer()
    {
        // Command-owned layers are owned by their bound command; they are removed by unbinding
        // the command (the dynamic-text manager's orphan sweep), never via the editor.
        if (_selectedLayer == null || _selectedLayer.IsCommandOwned) return;
        var idx = ButtonData.Layers.IndexOf(_selectedLayer);
        ButtonData.Layers.Remove(_selectedLayer);
        // Prefer the item that moved into the freed slot (the one below); fall back
        // to the new last item (the one above) when the removed layer was last.
        var next = (idx < ButtonData.Layers.Count) ? ButtonData.Layers[idx]
            : (ButtonData.Layers.Count > 0 ? ButtonData.Layers[^1] : null);
        ReselectAfterMove(next);
    }

    private void MoveSelectedLayerUp()
    {
        if (_selectedLayer == null) return;
        var layer = _selectedLayer;
        var idx = ButtonData.Layers.IndexOf(layer);
        if (idx <= 0) return;
        ButtonData.Layers.Move(idx, idx - 1);
        ReselectAfterMove(layer);
    }

    private void MoveSelectedLayerDown()
    {
        if (_selectedLayer == null) return;
        var layer = _selectedLayer;
        var idx = ButtonData.Layers.IndexOf(layer);
        if (idx < 0 || idx >= ButtonData.Layers.Count - 1) return;
        ButtonData.Layers.Move(idx, idx + 1);
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
        b.IgnoreRefresh = true;
        try
        {
            b.Command = null;
            b.BackColor = Avalonia.Media.Colors.Black;
            b.Layers.Clear();
        }
        finally
        {
            b.IgnoreRefresh = false;
        }
        b.Refresh();
        SelectedLayer = null;

        // Reload the sequence strips from the now-cleared sources.
        BuildCommandSlots();
    }

    private void ButtonData_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TouchButton.Command))
        {
            // Re-scan dynamic-text/-image commands so a display command's layer appears (or its
            // orphaned layer disappears) immediately while the editor is open, instead of only
            // after it closes. The strip-canvas surface is not a real page button, so skip it.
            if (!IsStripCanvas)
                Avalonia.Threading.Dispatcher.UIThread.Post(() => _dynamicTextManager.Rescan());
        }
    }

    private void ButtonData_ItemChanged(object sender, EventArgs e)
    {
        // ItemChanged may fire on a background thread (e.g. dynamic-text timer).
        // Dispatch to the UI thread so the bitmap swap and property notifications
        // are observed by Avalonia bindings.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            UpdateEditorPreview();
            UpdateSelectionBounds();
        });
    }

    private void UpdateEditorPreview()
    {
        if (ButtonData == null || _config == null) return;
        // Segment dividers only make sense (and are only drawn) for a free-draw side
        // strip; the renderer further gates them on the grid toggle.
        var segmentCount = IsSegmentCommandMode ? RotaryButtonPage.StripSegmentCount : 0;
        EditorPreview = BitmapHelper.RenderEditorCanvas(
            ButtonData, _config, EditorCanvasSize, DeviceWidth, DeviceHeight, ShowGrid, GridStepDevice,
            segmentCount);
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
            _selectedLayer, EditorCanvasSize, DeviceWidth, DeviceHeight);

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

            ButtonData.ItemChanged -= ButtonData_ItemChanged;
            ButtonData.PropertyChanged -= ButtonData_PropertyChanged;
        }

        _sideStripRegistry.ProvidersChanged -= OnStripProvidersChanged;

        foreach (var slot in CommandSlots)
            slot.Cleanup();
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
