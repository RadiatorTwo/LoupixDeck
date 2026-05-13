using System.Collections.ObjectModel;
using System.Windows.Input;
using LoupixDeck.Models;
using LoupixDeck.Models.Argus;
using LoupixDeck.Models.Layers;
using LoupixDeck.Services;
using LoupixDeck.Services.Argus;
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
        }

        ButtonData = parameter;

        if (ButtonData != null)
        {
            ButtonData.ItemChanged += ButtonData_ItemChanged;
            UpdateEditorPreview();
        }
    }

    private readonly IObsController _obs;
    private readonly ElgatoDevices _elgatoDevices;
    private readonly ICoolerControlApiController _coolercontrol;
    private readonly ISysCommandService _sysCommandService;
    private readonly ICommandBuilder _commandBuilder;
    private readonly IArgusMonitorService _argusMonitor;
    private readonly IAssetService _assetService;
    private readonly LoupedeckConfig _config;

    public const int EditorCanvasSize = 600;
    public const int EditorFrameSize = 300;
    public const int DeviceSize = 90;

    /// <summary>Editor → device coordinate factor (canvas pixels per device pixel).</summary>
    public static double EditorToDeviceScale => (double)EditorFrameSize / DeviceSize;


    public ICommand AddImageLayerCommand { get; }
    public ICommand AddTextLayerCommand { get; }
    public ICommand RemoveLayerCommand { get; }
    public ICommand MoveLayerUpCommand { get; }
    public ICommand MoveLayerDownCommand { get; }
    public TouchButton ButtonData { get; set; }

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

    public const double HandleSize = 10;
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

    private MenuEntry _elgatoKeyLightMenu;

    public TouchButtonSettingsViewModel(IObsController obs,
        ElgatoDevices elgatoDevices,
        ICoolerControlApiController coolercontrol,
        ISysCommandService sysCommandService,
        ICommandBuilder commandBuilder,
        IArgusMonitorService argusMonitor,
        IAssetService assetService,
        LoupedeckConfig config)
    {
        _obs = obs;
        _elgatoDevices = elgatoDevices;
        _coolercontrol = coolercontrol;
        _sysCommandService = sysCommandService;
        _commandBuilder = commandBuilder;
        _argusMonitor = argusMonitor;
        _assetService = assetService;
        _config = config;

        AddImageLayerCommand = new AsyncRelayCommand(AddImageLayer);
        AddTextLayerCommand = new RelayCommand(AddTextLayer);
        RemoveLayerCommand = new RelayCommand(RemoveSelectedLayer);
        MoveLayerUpCommand = new RelayCommand(MoveSelectedLayerUp);
        MoveLayerDownCommand = new RelayCommand(MoveSelectedLayerDown);

        SystemCommandMenus = new ObservableCollection<MenuEntry>();
    }

    public Task InitializeAsync()
    {
        return CreateSystemMenu();
    }

    private async Task CreateSystemMenu()
    {
        CreatePagesMenu();

        // Macros are only available on Linux
        if (OperatingSystem.IsLinux())
        {
            CreateMacroMenu();
        }

        // OBS and CoolerControl menus add their group entries synchronously
        // and populate scenes/modes asynchronously, so order stays stable
        // while both network calls run in parallel.
        var obsTask = CreateObsMenu();
        CreateElgatoMenu();
        var coolerTask = CreateCoolerControlMenu();
        CreateDynamicTextMenu();
        CreateArgusMonitorMenu();
        CreateAudioMenu();

        await Task.WhenAll(obsTask, coolerTask);
    }

    private void CreateAudioMenu()
    {
        var commands = _sysCommandService.GetCommandInfos().Where(ci => ci.Group == "Audio");

        var groupMenu = new MenuEntry("Audio", string.Empty);

        foreach (var command in commands)
        {
            groupMenu.Children.Add(new MenuEntry(command.DisplayName, command.CommandName));
        }

        SystemCommandMenus.Add(groupMenu);
    }

    private void CreateArgusMonitorMenu()
    {
        var groupMenu = new MenuEntry("Argus Monitor", string.Empty);

        var sensors = _argusMonitor.Sensors;
        if (!_argusMonitor.IsAvailable || sensors.Count == 0)
        {
            groupMenu.Children.Add(new MenuEntry("Argus Monitor not available", string.Empty));
            SystemCommandMenus.Add(groupMenu);
            return;
        }

        foreach (var typeGroup in sensors
                     .Where(s => s.Type != ArgusSensorType.Invalid)
                     .GroupBy(s => s.Type)
                     .OrderBy(g => g.Key.ToString()))
        {
            var typeMenu = new MenuEntry(typeGroup.Key.ToString(), string.Empty);

            foreach (var sensor in typeGroup.OrderBy(s => s.SensorIndex))
            {
                var label = string.IsNullOrWhiteSpace(sensor.Label)
                    ? $"#{sensor.SensorIndex}"
                    : sensor.Label;

                typeMenu.Children.Add(new MenuEntry(
                    label,
                    "Argus.Sensor",
                    parentName: null,
                    parameters: new Dictionary<string, string>
                    {
                        { "Sensor", $"{sensor.Type}:{sensor.SensorIndex}" }
                    }));
            }

            groupMenu.Children.Add(typeMenu);
        }

        SystemCommandMenus.Add(groupMenu);
    }

    private void CreateDynamicTextMenu()
    {
        var commands = _sysCommandService.GetCommandInfos().Where(ci => ci.Group == "Dynamic Text");

        var groupMenu = new MenuEntry("Dynamic Text", string.Empty);

        foreach (var command in commands)
        {
            groupMenu.Children.Add(new MenuEntry(command.DisplayName, command.CommandName));
        }

        SystemCommandMenus.Add(groupMenu);
    }

    private void CreatePagesMenu()
    {
        // Get Only Pages Commands
        var commands = _sysCommandService.GetCommandInfos().Where(ci => ci.Group == "Pages");

        var groupMenu = new MenuEntry("Pages", string.Empty);

        foreach (var command in commands)
        {
            groupMenu.Children.Add(new MenuEntry(command.DisplayName, command.CommandName));
        }

        SystemCommandMenus.Add(groupMenu);
    }

    private async Task CreateObsMenu()
    {
        var commands = _sysCommandService.GetCommandInfos().Where(ci => ci.Group == "OBS");

        var groupMenu = new MenuEntry("OBS", string.Empty);

        foreach (var command in commands)
        {
            if (command.CommandName == "System.ObsSetScene")
                continue;

            groupMenu.Children.Add(new MenuEntry(command.DisplayName, command.CommandName));
        }

        var scenesMenu = new MenuEntry("Scenes", string.Empty);
        groupMenu.Children.Add(scenesMenu);
        SystemCommandMenus.Add(groupMenu);

        try
        {
            var scenes = await _obs.GetScenes();

            foreach (var scene in scenes)
            {
                scenesMenu.Children.Add(new MenuEntry(scene.Name, $"System.ObsSetScene"));
            }
        }
        catch (Exception ex)
        {
            // If OBS is not connected, add an error entry to inform the user
            scenesMenu.Children.Add(new MenuEntry($"OBS not connected: {ex.Message}", string.Empty));
        }
    }

    private void CreateMacroMenu()
    {
        var commands = _sysCommandService.GetCommandInfos()
            .Where(ci => ci.Group == "Macros")
            .OrderBy(ci => ci.Group);

        var groupMenu = new MenuEntry("Macros", string.Empty);

        foreach (var command in commands)
        {
            groupMenu.Children.Add(new MenuEntry(command.DisplayName, command.CommandName));
        }

        SystemCommandMenus.Add(groupMenu);
    }

    private void CreateElgatoMenu()
    {
        _elgatoKeyLightMenu = new MenuEntry("Elgato Keylights", string.Empty);

        foreach (var keyLight in _elgatoDevices.KeyLights)
        {
            AddKeyLightMenuEntry(keyLight);
        }

        _elgatoDevices.KeyLightAdded += KeyLightAdded;

        SystemCommandMenus.Add(_elgatoKeyLightMenu);
    }

    private async Task CreateCoolerControlMenu()
    {
        var commands = _sysCommandService.GetCommandInfos().Where(ci => ci.Group == "Cooler Control");

        var groupMenu = new MenuEntry("Cooler Control", string.Empty);

        foreach (var command in commands)
        {
            if (command.CommandName == "System.CoolerControlSetMode")
                continue;

            groupMenu.Children.Add(new MenuEntry(command.DisplayName, command.CommandName));
        }

        var modesMenu = new MenuEntry("Modes", string.Empty);
        groupMenu.Children.Add(modesMenu);
        SystemCommandMenus.Add(groupMenu);

        try
        {
            var modes = await _coolercontrol.GetModes();

            foreach (var mode in modes)
            {
                modesMenu.Children.Add(
                    new MenuEntry(mode["name"]?.ToString(),
                        $"System.CoolerControlSetMode",
                        null,
                        new Dictionary<string, string>() { { "UID", mode["uid"]?.ToString() ?? string.Empty } })
                );
            }
        }
        catch (Exception ex)
        {
            // If connection fails, add an error entry to inform the user
            modesMenu.Children.Add(new MenuEntry($"Connection failed: {ex.Message}", string.Empty));
        }
    }

    private void KeyLightAdded(object sender, KeyLight e)
    {
        AddKeyLightMenuEntry(e);
    }

    private void AddKeyLightMenuEntry(KeyLight keyLight)
    {
        var checkKeyLight = _elgatoKeyLightMenu.Children.FirstOrDefault(kl => kl.Name == keyLight.DisplayName);

        if (checkKeyLight != null)
            return;

        var keyLightGroup = new MenuEntry(keyLight.DisplayName, null);

        var commands = _sysCommandService.GetCommandInfos().Where(ci => ci.Group == "Elgato Keylights");

        foreach (var command in commands)
        {
            keyLightGroup.Children.Add(new MenuEntry(command.DisplayName, command.CommandName, keyLight.DisplayName));
        }

        _elgatoKeyLightMenu.Children.Add(keyLightGroup);
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

        ButtonData.Command += formattedCommand;
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
        }
    }
}