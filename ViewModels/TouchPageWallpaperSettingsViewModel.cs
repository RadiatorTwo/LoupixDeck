using System.Collections.ObjectModel;
using System.Windows.Input;
using LoupixDeck.Models;
using LoupixDeck.Services;
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
    // Asset sub-folder for page wallpapers — kept in sync with WallpaperAssetMigrator.
    private const string WallpapersSubFolder = "wallpapers";

    private readonly IAssetService _assetService;

    private TouchButtonPage _targetPage;
    // The original (un-scaled) image, loaded from the asset folder. Scaling is
    // applied on top of this so the wallpaper stays fully re-editable.
    private SKBitmap _wallpaperBitmap;

    // Snapshot for Cancel — restore the page's persisted state.
    private string _originalAssetPath;
    private int _originalScaling;
    private int _originalPositionX;
    private int _originalPositionY;
    private BitmapHelper.ScalingOption _originalScalingOption;
    private double _originalOpacity;

    // Suppresses ApplyScaling while Initialize seeds the sliders from the page.
    private bool _suppressApply;

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

    public TouchPageWallpaperSettingsViewModel(IAssetService assetService)
    {
        _assetService = assetService;
        SelectImageCommand = new AsyncRelayCommand(SelectImage);
        RemoveWallpaperCommand = new RelayCommand(RemoveWallpaper);
        ConfirmCommand = new RelayCommand(ConfirmDialog);
        CancelCommand = new RelayCommand(CancelDialog);
    }

    public override void Initialize(TouchButtonPage parameter)
    {
        _targetPage = parameter;

        // Seed the sliders from the page's persisted parameters so the wallpaper
        // is re-editable. Suppress baking while assigning — we bake once below.
        _suppressApply = true;
        WallpaperScaling = parameter?.WallpaperScaling ?? 100;
        WallpaperPositionX = parameter?.WallpaperPositionX ?? 0;
        WallpaperPositionY = parameter?.WallpaperPositionY ?? 0;
        SelectedWallpaperScalingOption = parameter?.WallpaperScalingOption ?? BitmapHelper.ScalingOption.Fit;
        _suppressApply = false;

        // Snapshot for Cancel.
        _originalAssetPath = parameter?.WallpaperAssetPath;
        _originalScaling = WallpaperScaling;
        _originalPositionX = WallpaperPositionX;
        _originalPositionY = WallpaperPositionY;
        _originalScalingOption = SelectedWallpaperScalingOption;
        _originalOpacity = parameter?.WallpaperOpacity ?? 0;

        // Load the original image and render the preview from the seeded parameters.
        _wallpaperBitmap = string.IsNullOrWhiteSpace(parameter?.WallpaperAssetPath)
            ? null
            : _assetService.Load(parameter.WallpaperAssetPath);
        ApplyScaling();

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
        set { if (SetProperty(ref _selectedWallpaperScalingOption, value) && !_suppressApply) ApplyScaling(); }
    }

    private int _wallpaperScaling = 100;
    public int WallpaperScaling
    {
        get => _wallpaperScaling;
        set { if (SetProperty(ref _wallpaperScaling, value) && !_suppressApply) ApplyScaling(); }
    }

    private int _wallpaperPositionX;
    public int WallpaperPositionX
    {
        get => _wallpaperPositionX;
        set { if (SetProperty(ref _wallpaperPositionX, value) && !_suppressApply) ApplyScaling(); }
    }

    private int _wallpaperPositionY;
    public int WallpaperPositionY
    {
        get => _wallpaperPositionY;
        set { if (SetProperty(ref _wallpaperPositionY, value) && !_suppressApply) ApplyScaling(); }
    }

    private async Task SelectImage()
    {
        var result = await FileDialogHelper.OpenFileDialog();
        if (string.IsNullOrEmpty(result) || !File.Exists(result)) return;

        // Copy the original into the asset folder (content-hashed) and reference it
        // by relative path, like image layers — but kept under a dedicated
        // "wallpapers" sub-folder so page wallpapers stay separated from layers.
        var relative = _assetService.Import(result, WallpapersSubFolder);
        if (string.IsNullOrEmpty(relative)) return;

        _targetPage.WallpaperAssetPath = relative;
        _wallpaperBitmap = _assetService.Load(relative);
        ApplyScaling();
    }

    private void RemoveWallpaper()
    {
        if (_targetPage == null) return;
        _targetPage.WallpaperAssetPath = null;
        _targetPage.WallpaperOpacity = 0;
        _targetPage.Wallpaper = null;
        _wallpaperBitmap = null;
        OnPropertyChanged(nameof(Wallpaper));
        OnPropertyChanged(nameof(WallpaperOpacity));
    }

    private void ApplyScaling()
    {
        if (_targetPage == null) return;

        // Persist the scaling parameters on the page (re-editable across sessions).
        _targetPage.WallpaperScaling = WallpaperScaling;
        _targetPage.WallpaperPositionX = WallpaperPositionX;
        _targetPage.WallpaperPositionY = WallpaperPositionY;
        _targetPage.WallpaperScalingOption = SelectedWallpaperScalingOption;

        if (_wallpaperBitmap == null) return;

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
            _targetPage.WallpaperAssetPath = _originalAssetPath;
            _targetPage.WallpaperScaling = _originalScaling;
            _targetPage.WallpaperPositionX = _originalPositionX;
            _targetPage.WallpaperPositionY = _originalPositionY;
            _targetPage.WallpaperScalingOption = _originalScalingOption;
            _targetPage.WallpaperOpacity = _originalOpacity;
            _targetPage.Wallpaper = null; // drop the preview cache → re-bake from restored params
        }
        Cancel();
        CloseRequested?.Invoke();
    }
}
