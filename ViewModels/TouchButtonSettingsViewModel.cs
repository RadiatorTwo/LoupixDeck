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

        OnPropertyChanged(nameof(ButtonNumber));
        OnPropertyChanged(nameof(ButtonLabel));
        NotifyCommandChanged();
    }

    private readonly ICommandBuilder _commandBuilder;
    private readonly IMenuTreeBuilder _menuTreeBuilder;
    private readonly IAssetService _assetService;
    private readonly IDialogService _dialogService;
    private readonly LoupedeckConfig _config;

    public const int EditorCanvasSize = 600;
    public const int EditorFrameSize = 300;
    public const int DeviceSize = 90;

    /// <summary>Editor → device coordinate factor (canvas pixels per device pixel).</summary>
    public static double EditorToDeviceScale => (double)EditorFrameSize / DeviceSize;


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
    public string CanvasSizeText => $"{DeviceSize} × {DeviceSize} px";

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

    public TouchButtonSettingsViewModel(
        ICommandBuilder commandBuilder,
        IMenuTreeBuilder menuTreeBuilder,
        IAssetService assetService,
        IDialogService dialogService,
        LoupedeckConfig config)
    {
        _commandBuilder = commandBuilder;
        _menuTreeBuilder = menuTreeBuilder;
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

        SystemCommandMenus = new ObservableCollection<MenuEntry>();
    }

    public async Task InitializeAsync()
    {
        await _menuTreeBuilder.BuildInto(SystemCommandMenus, ButtonTargets.TouchButton);
    }

    private async Task AddImageLayer()
    {
        var path = await FileDialogHelper.OpenFileDialog();
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        var relative = _assetService.Import(path);
        if (string.IsNullOrEmpty(relative)) return;

        var layer = new ImageLayer
        {
            Name = Path.GetFileNameWithoutExtension(path),
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
            Name = "Text",
            Text = "Text",
            BoxWidth = DeviceSize,
            BoxHeight = DeviceSize
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
            Name = def.DisplayName,
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
        SelectedLayer = (idx < ButtonData.Layers.Count) ? ButtonData.Layers[idx]
            : (ButtonData.Layers.Count > 0 ? ButtonData.Layers[^1] : null);
    }

    private void MoveSelectedLayerUp()
    {
        if (_selectedLayer == null) return;
        var idx = ButtonData.Layers.IndexOf(_selectedLayer);
        if (idx <= 0) return;
        ButtonData.Layers.Move(idx, idx - 1);
    }

    private void MoveSelectedLayerDown()
    {
        if (_selectedLayer == null) return;
        var idx = ButtonData.Layers.IndexOf(_selectedLayer);
        if (idx < 0 || idx >= ButtonData.Layers.Count - 1) return;
        ButtonData.Layers.Move(idx, idx + 1);
    }

    public void InsertCommand(MenuEntry menuEntry)
    {
        var formattedCommand = _commandBuilder.CreateCommandFromMenuEntry(menuEntry);
        ButtonData.Command = Utils.CommandChain.Append(ButtonData.Command, formattedCommand);
        NotifyCommandChanged();
    }

    /// <summary>
    /// Clears only the assigned command of the current button — leaves layers,
    /// colors and all other settings untouched (unlike <see cref="ClearButton"/>).
    /// </summary>
    public void ClearCommandOnly()
    {
        if (ButtonData == null) return;
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
            ButtonData, _config, EditorCanvasSize, EditorFrameSize);
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
            _selectedLayer, EditorCanvasSize, EditorFrameSize);

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
    }
}
