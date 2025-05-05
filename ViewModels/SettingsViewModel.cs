using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Media;
using LoupixDeck.Models;
using LoupixDeck.Services;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;
using OBSWebsocketDotNet.Communication;
using SkiaSharp;

namespace LoupixDeck.ViewModels;

public class SettingsViewModel : DialogViewModelBase<DialogResult>
{
    public LoupedeckConfig Config { get; }
    private readonly IObsController _obs;
    public ICommand SaveObsCommand { get; }
    public ICommand TestConnectionCommand { get; }
    public ICommand SelectImageButtonCommand { get; }
    public ICommand NavigateCommand { get; }
    
    private SKBitmap _wallpaperBitmap = null;

    private SettingsView _currentView;

    public SettingsView CurrentView
    {
        get => _currentView;
        set => SetProperty(ref _currentView, value);
    }

    public ObsConfig ObsConfig { get; }
    
    public ObservableCollection<BitmapHelper.ScalingOption> WallpaperScalingOptions { get; } =
    [
        BitmapHelper.ScalingOption.None,
        BitmapHelper.ScalingOption.Fill,
        BitmapHelper.ScalingOption.Fit,
        BitmapHelper.ScalingOption.Stretch,
        BitmapHelper.ScalingOption.Tile,
        BitmapHelper.ScalingOption.Center,
        // BitmapHelper.ScalingOption.CropToFill
    ];

    private BitmapHelper.ScalingOption _selectedWallpaperScalingOption = BitmapHelper.ScalingOption.Fit;

    public BitmapHelper.ScalingOption SelectedWallpaperScalingOption
    {
        get => _selectedWallpaperScalingOption;
        set
        {
            SetProperty(ref _selectedWallpaperScalingOption, value);
            
            if (_wallpaperBitmap == null) return;
            
            var scaledImage = BitmapHelper.ScaleAndPositionBitmap(_wallpaperBitmap, 480, 270, 100, 0, 0, value);
            Config.Wallpaper = scaledImage.ToRenderTargetBitmap();
        }
    }

    public SettingsViewModel(LoupedeckConfig config, IObsController obs)
    {
        Config = config;
        SaveObsCommand = new RelayCommand(SaveObs);
        TestConnectionCommand = new RelayCommand(TestConnection);
        SelectImageButtonCommand = new AsyncRelayCommand(SelectImageButton_Click);

        NavigateCommand = new RelayCommand<SettingsView>(Navigate);
        CurrentView = SettingsView.General;

        ConnectionTestVisible = true;

        ObsConfig = ObsConfig.LoadConfig();
        _obs = obs;

        _obs.Connected += ObsConnected;
        _obs.Disconnected += ObsDisconnected;
    }

    private void ObsConnected(object sender, EventArgs e)
    {
        ConnectionResult = "Successfully connected";
        ConnectionTestVisible = true;
        TextColor = Colors.Green;
    }

    private void ObsDisconnected(object sender, ObsDisconnectionInfo e)
    {
        ConnectionResult = $"Error: {e.WebsocketDisconnectionInfo.CloseStatusDescription}";
        ConnectionTestVisible = true;
        TextColor = Colors.Red;
    }

    private bool _connectionTestVisible;

    public bool ConnectionTestVisible
    {
        get => _connectionTestVisible;
        set => SetProperty(ref _connectionTestVisible, value);
    }

    private Color _textColor = Colors.Blue;

    public Color TextColor
    {
        get => _textColor;
        set => SetProperty(ref _textColor, value);
    }

    private string _connectionResult;

    public string ConnectionResult
    {
        get => _connectionResult;
        set => SetProperty(ref _connectionResult, value);
    }

    private void SaveObs()
    {
        ObsConfig.SaveConfig();
    }

    private void TestConnection()
    {
        _obs.Connect(ObsConfig.Ip, ObsConfig.Port, ObsConfig.Password);
    }

    private async Task SelectImageButton_Click()
    {
        var result = await FileDialogHelper.OpenFileDialog();

        if (result == null || !File.Exists(result)) return;

        _wallpaperBitmap = SKBitmap.Decode(result);

        var scaledImage = BitmapHelper.ScaleAndPositionBitmap(_wallpaperBitmap, 480, 270, 100, 0, 0, SelectedWallpaperScalingOption);

        Config.Wallpaper = scaledImage.ToRenderTargetBitmap();
    }

    private void Navigate(SettingsView settingsPage)
    {
        CurrentView = settingsPage;
    }
}