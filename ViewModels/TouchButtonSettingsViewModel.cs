using System.Collections.ObjectModel;
using System.Windows.Input;
using LoupixDeck.Models;
using LoupixDeck.Models.Converter;
using LoupixDeck.Models.Layers;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services;
using LoupixDeck.Services.Commands;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;
using SkiaSharp;

namespace LoupixDeck.ViewModels;

public class TouchButtonSettingsViewModel : DialogViewModelBase<TouchButton, DialogResult>, IAsyncInitViewModel

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
            UpdateEditorPreview();

            _selectedVibrationPattern = VibrationPatterns.FirstOrDefault(
                p => p.Value == ButtonData.VibrationPattern);
            OnPropertyChanged(nameof(SelectedVibrationPattern));
        }

        LoadSegments();

        OnPropertyChanged(nameof(ButtonNumber));
        OnPropertyChanged(nameof(ButtonLabel));
        NotifyCommandChanged();
    }

    private readonly ICommandBuilder _commandBuilder;
    private readonly IMenuTreeBuilder _menuTreeBuilder;
    private readonly ICommandRegistry _commandRegistry;
    private readonly IAssetService _assetService;
    private readonly IDialogService _dialogService;
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

    // ───────── Side-strip (Razer) mode ─────────

    private RotaryButtonPage _stripPage;

    /// <summary>
    /// True when this editor instance is editing a Razer side-strip canvas (rather
    /// than an ordinary grid touch button). Drives the strip-mode picker and the
    /// draw-mode gate; false for normal buttons, so their behaviour is unchanged.
    /// </summary>
    public bool IsStripCanvas => _stripPage != null;

    /// <summary>Strip modes offered in the editor's picker. PluginOverride is hidden
    /// until phase 2b.</summary>
    public IReadOnlyList<StripMode> AvailableStripModes { get; } =
        new[] { StripMode.Segmented, StripMode.FreeDraw };

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

            // Repaint the strip immediately via the canvas's live-redraw subscription
            // (the controller reads the new mode and renders labels vs. canvas), instead
            // of waiting for the dialog to close.
            _stripPage.StripCanvas?.Refresh();
        }
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
        OnPropertyChanged(nameof(StripMode));
        OnPropertyChanged(nameof(CanEditCanvas));
        OnPropertyChanged(nameof(IsDrawDisabledHintVisible));
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


    public ICommand AddImageLayerCommand { get; }
    public ICommand AddTextLayerCommand { get; }
    public ICommand AddSymbolLayerCommand { get; }
    public ICommand RemoveLayerCommand { get; }
    public ICommand MoveLayerUpCommand { get; }
    public ICommand MoveLayerDownCommand { get; }
    public ICommand ClearCommandCommand { get; }
    public TouchButton ButtonData { get; set; }

    /// <summary>1-based button number shared by the window title and the
    /// properties panel so both read identically; the underlying Index stays
    /// 0-based.</summary>
    public int ButtonNumber => (ButtonData?.Index ?? 0) + 1;

    /// <summary>Window title, e.g. "Touch Button 1".</summary>
    public string ButtonLabel => $"Touch Button {ButtonNumber}";

    /// <summary>True when the button has a non-empty command assigned.</summary>
    public bool HasCommand => !string.IsNullOrWhiteSpace(ButtonData?.Command);

    /// <summary>Read-only summary shown in the bottom command bar — the raw,
    /// chained command string, or a placeholder when none is assigned.</summary>
    public string CommandSummary => HasCommand ? ButtonData.Command : "No command assigned";

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
            UpdateSelectionBounds();
        }
    }

    public ImageLayer SelectedImageLayer => _selectedLayer as ImageLayer;
    public TextLayer SelectedTextLayer => _selectedLayer as TextLayer;

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
    private double Hx(double cx) => cx - HandleSize / 2.0;
    private double Hy(double cy) => cy - HandleSize / 2.0;
    public double HandleNwLeft => Hx(SelectionLeft);
    public double HandleNwTop  => Hy(SelectionTop);
    public double HandleNeLeft => Hx(SelectionLeft + SelectionWidth);
    public double HandleNeTop  => Hy(SelectionTop);
    public double HandleSwLeft => Hx(SelectionLeft);
    public double HandleSwTop  => Hy(SelectionTop + SelectionHeight);
    public double HandleSeLeft => Hx(SelectionLeft + SelectionWidth);
    public double HandleSeTop  => Hy(SelectionTop + SelectionHeight);
    public double HandleNLeft  => Hx(SelectionLeft + SelectionWidth / 2.0);
    public double HandleNTop   => Hy(SelectionTop);
    public double HandleSLeft  => Hx(SelectionLeft + SelectionWidth / 2.0);
    public double HandleSTop   => Hy(SelectionTop + SelectionHeight);
    public double HandleWLeft  => Hx(SelectionLeft);
    public double HandleWTop   => Hy(SelectionTop + SelectionHeight / 2.0);
    public double HandleELeft  => Hx(SelectionLeft + SelectionWidth);
    public double HandleETop   => Hy(SelectionTop + SelectionHeight / 2.0);

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

    /// <summary>The button's command chain as individual, editable cards. The raw
    /// <see cref="TouchButton.Command"/> string stays the persisted source of truth;
    /// this collection is a view over it that is recomposed on every edit.</summary>
    public ObservableCollection<CommandSegment> Commands { get; } = [];

    public TouchButtonSettingsViewModel(
        ICommandBuilder commandBuilder,
        IMenuTreeBuilder menuTreeBuilder,
        ICommandRegistry commandRegistry,
        IAssetService assetService,
        IDialogService dialogService,
        LoupedeckConfig config)
    {
        _commandBuilder = commandBuilder;
        _menuTreeBuilder = menuTreeBuilder;
        _commandRegistry = commandRegistry;
        _assetService = assetService;
        _dialogService = dialogService;
        _config = config;

        AddImageLayerCommand = new AsyncRelayCommand(AddImageLayer);
        AddTextLayerCommand = new RelayCommand(AddTextLayer);
        AddSymbolLayerCommand = new AsyncRelayCommand(AddSymbolLayer);
        RemoveLayerCommand = new RelayCommand(RemoveSelectedLayer);
        MoveLayerUpCommand = new RelayCommand(MoveSelectedLayerUp);
        MoveLayerDownCommand = new RelayCommand(MoveSelectedLayerDown);
        ClearCommandCommand = new RelayCommand(ClearCommandOnly);

        // Keep the 1-based sequence numbers on the chips in sync with the
        // collection (insert, remove, move, clear, initial load).
        Commands.CollectionChanged += (_, _) => RenumberSegments();

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

    private void AddTextLayer()
    {
        var layer = new TextLayer
        {
            Name = GetUniqueLayerName("Text"),
            Text = "Text",
            BoxWidth = DeviceWidth,
            BoxHeight = DeviceHeight
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
        var layer = new SymbolLayer
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
        if (_selectedLayer == null) return;
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

    /// <summary>Parses <see cref="TouchButton.Command"/> into editable segment cards.
    /// Does not write back — opening (and closing without edits) leaves the persisted
    /// string byte-for-byte unchanged.</summary>
    private void LoadSegments()
    {
        foreach (var segment in Commands)
            segment.Changed -= OnSegmentChanged;
        Commands.Clear();

        if (ButtonData == null) return;

        foreach (var raw in Utils.CommandStringParser.SplitChain(ButtonData.Command))
            Commands.Add(CreateSegment(raw));
    }

    /// <summary>Builds a <see cref="CommandSegment"/> from a raw segment, resolving its
    /// <see cref="Commands.Base.CommandInfo"/> when the command name is a known system command.</summary>
    private CommandSegment CreateSegment(string raw)
    {
        var name = Utils.CommandStringParser.GetName(raw);
        var info = _commandRegistry.Get(name)?.Info;
        var segment = CommandSegment.Create(_commandBuilder, info, raw);
        segment.Changed += OnSegmentChanged;
        return segment;
    }

    private void OnSegmentChanged(object sender, EventArgs e) => RebuildCommandString();

    /// <summary>Reassigns the 1-based <see cref="CommandSegment.Position"/> shown
    /// on every chip in the sequence strip.</summary>
    private void RenumberSegments()
    {
        for (var i = 0; i < Commands.Count; i++)
            Commands[i].Position = i + 1;
    }

    /// <summary>Recomposes the persisted <c>&amp;&amp;</c>-joined command string from the
    /// current card order/values. Called after every add / remove / reorder / edit.</summary>
    private void RebuildCommandString()
    {
        if (ButtonData == null) return;

        var joined = string.Join(" && ",
            Commands.Select(s => s.Raw).Where(r => !string.IsNullOrWhiteSpace(r)));

        ButtonData.Command = string.IsNullOrWhiteSpace(joined) ? null : joined;
        NotifyCommandChanged();
    }

    /// <summary>Appends a command (double-click in the tree) to the end of the chain.</summary>
    public void InsertCommand(MenuEntry menuEntry) => InsertCommandAt(menuEntry, Commands.Count);

    /// <summary>Inserts a command (drag from the tree) at the given card index.</summary>
    public void InsertCommandAt(MenuEntry menuEntry, int index)
    {
        if (ButtonData == null || menuEntry == null) return;

        var formattedCommand = _commandBuilder.CreateCommandFromMenuEntry(menuEntry);
        if (string.IsNullOrWhiteSpace(formattedCommand)) return;

        index = Math.Clamp(index, 0, Commands.Count);
        Commands.Insert(index, CreateSegment(formattedCommand));
        RebuildCommandString();
    }

    public void RemoveSegment(CommandSegment segment)
    {
        if (segment == null || !Commands.Remove(segment)) return;
        segment.Changed -= OnSegmentChanged;
        RebuildCommandString();
    }

    public void MoveSegment(int from, int to)
    {
        if (from < 0 || from >= Commands.Count) return;
        to = Math.Clamp(to, 0, Commands.Count - 1);
        if (from == to) return;
        Commands.Move(from, to);
        RebuildCommandString();
    }

    /// <summary>
    /// Clears only the assigned command of the current button — leaves layers,
    /// colors and all other settings untouched (unlike <see cref="ClearButton"/>).
    /// </summary>
    public void ClearCommandOnly()
    {
        if (ButtonData == null) return;
        foreach (var segment in Commands)
            segment.Changed -= OnSegmentChanged;
        Commands.Clear();
        ButtonData.Command = null;
        NotifyCommandChanged();
    }

    private void NotifyCommandChanged()
    {
        OnPropertyChanged(nameof(HasCommand));
        OnPropertyChanged(nameof(CommandSummary));
    }

    /// <summary>
    /// Resets the touch button to a blank default state — clears command, text, image and
    /// all visual settings. Triggers a single redraw at the end via Refresh().
    /// </summary>
    public void ClearButton()
    {
        if (ButtonData == null) return;

        foreach (var segment in Commands)
            segment.Changed -= OnSegmentChanged;
        Commands.Clear();

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
        NotifyCommandChanged();
    }

    private void ButtonData_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Keep the bottom summary bar in sync when the command is edited
        // directly (Commands tab text box) or replaced programmatically.
        if (e.PropertyName == nameof(TouchButton.Command))
            Avalonia.Threading.Dispatcher.UIThread.Post(NotifyCommandChanged);
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
        EditorPreview = BitmapHelper.RenderEditorCanvas(
            ButtonData, _config, EditorCanvasSize, DeviceWidth, DeviceHeight, ShowGrid, GridStepDevice);
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
            ButtonData.ItemChanged -= ButtonData_ItemChanged;
            ButtonData.PropertyChanged -= ButtonData_PropertyChanged;
        }

        foreach (var segment in Commands)
            segment.Changed -= OnSegmentChanged;
    }
}
