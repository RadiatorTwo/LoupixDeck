using System.Collections.ObjectModel;
using System.Windows.Input;
using LoupixDeck.Models;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;
using SkiaSharp;
// Utils.RelayCommand executes via Task.Run (background thread) — that would
// raise CloseRequested off the UI thread and crash Window.Close(). Use the
// CommunityToolkit synchronous RelayCommand for dialog buttons.
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

namespace LoupixDeck.ViewModels;

public class TouchPageWallpaperSettingsViewModel : DialogViewModelBase<TouchButtonPage, DialogResult>
{
    private TouchButtonPage _targetPage;
    private SKBitmap _wallpaperBitmap;
    private SKBitmap _originalWallpaper;
    private double _originalOpacity;

    public ICommand SelectImageCommand { get; }
    public ICommand RemoveWallpaperCommand { get; }
    public ICommand ConfirmCommand { get; }
    public ICommand CancelCommand { get; }

    public event Action CloseRequested;

    public ObservableCollection<BitmapHelper.ScalingOption> WallpaperScalingOptions { get; } =
    [
        BitmapHelper.ScalingOption.None,
        BitmapHelper.ScalingOption.Fill,
        BitmapHelper.ScalingOption.Fit,
        BitmapHelper.ScalingOption.Stretch,
        BitmapHelper.ScalingOption.Tile,
        BitmapHelper.ScalingOption.Center,
    ];

    public TouchPageWallpaperSettingsViewModel()
    {
        SelectImageCommand = new AsyncRelayCommand(SelectImage);
        RemoveWallpaperCommand = new RelayCommand(RemoveWallpaper);
        ConfirmCommand = new RelayCommand(ConfirmDialog);
        CancelCommand = new RelayCommand(CancelDialog);
    }

    public override void Initialize(TouchButtonPage parameter)
    {
        _targetPage = parameter;
        _wallpaperBitmap = parameter?.Wallpaper;
        _originalWallpaper = parameter?.Wallpaper;
        _originalOpacity = parameter?.WallpaperOpacity ?? 0;

        OnPropertyChanged(nameof(PageName));
        OnPropertyChanged(nameof(Wallpaper));
        OnPropertyChanged(nameof(WallpaperOpacity));
    }

    public string PageName => _targetPage?.PageName ?? string.Empty;

    public SKBitmap Wallpaper
    {
        get => _targetPage?.Wallpaper;
        set
        {
            if (_targetPage != null && _targetPage.Wallpaper != value)
            {
                _targetPage.Wallpaper = value;
                OnPropertyChanged();
            }
        }
    }

    public double WallpaperOpacity
    {
        get => _targetPage?.WallpaperOpacity ?? 0;
        set
        {
            if (_targetPage != null && Math.Abs(_targetPage.WallpaperOpacity - value) > 0.0001)
            {
                _targetPage.WallpaperOpacity = value;
                OnPropertyChanged();
            }
        }
    }

    private BitmapHelper.ScalingOption _selectedWallpaperScalingOption = BitmapHelper.ScalingOption.Fit;
    public BitmapHelper.ScalingOption SelectedWallpaperScalingOption
    {
        get => _selectedWallpaperScalingOption;
        set { SetProperty(ref _selectedWallpaperScalingOption, value); ApplyScaling(); }
    }

    private int _wallpaperScaling = 100;
    public int WallpaperScaling
    {
        get => _wallpaperScaling;
        set { SetProperty(ref _wallpaperScaling, value); ApplyScaling(); }
    }

    private int _wallpaperPositionX;
    public int WallpaperPositionX
    {
        get => _wallpaperPositionX;
        set { SetProperty(ref _wallpaperPositionX, value); ApplyScaling(); }
    }

    private int _wallpaperPositionY;
    public int WallpaperPositionY
    {
        get => _wallpaperPositionY;
        set { SetProperty(ref _wallpaperPositionY, value); ApplyScaling(); }
    }

    private async Task SelectImage()
    {
        var result = await FileDialogHelper.OpenFileDialog();
        if (string.IsNullOrEmpty(result) || !File.Exists(result)) return;
        _wallpaperBitmap = SKBitmap.Decode(result);
        ApplyScaling();
    }

    private void RemoveWallpaper()
    {
        if (_targetPage == null) return;
        _targetPage.Wallpaper = null;
        _targetPage.WallpaperOpacity = 0;
        _wallpaperBitmap = null;
        OnPropertyChanged(nameof(Wallpaper));
        OnPropertyChanged(nameof(WallpaperOpacity));
    }

    private void ApplyScaling()
    {
        if (_wallpaperBitmap == null || _targetPage == null) return;

        var scaledImage = BitmapHelper.ScaleAndPositionBitmap(
            _wallpaperBitmap, 480, 270,
            WallpaperScaling, WallpaperPositionX, WallpaperPositionY,
            SelectedWallpaperScalingOption);

        _targetPage.Wallpaper = scaledImage;
        OnPropertyChanged(nameof(Wallpaper));
    }

    private void ConfirmDialog()
    {
        Confirm(new DialogResult(true));
        CloseRequested?.Invoke();
    }

    private void CancelDialog()
    {
        if (_targetPage != null)
        {
            _targetPage.Wallpaper = _originalWallpaper;
            _targetPage.WallpaperOpacity = _originalOpacity;
        }
        Cancel();
        CloseRequested?.Invoke();
    }
}
