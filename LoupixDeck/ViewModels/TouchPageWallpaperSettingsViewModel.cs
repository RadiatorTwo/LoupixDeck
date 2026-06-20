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

/// <summary>
/// Edits a touch page's wallpapers. Supports three independent targets — the main
/// 480×270 panel and (on devices with side strips) the left/right 60×270 side
/// displays. The left panel is a clickable device preview; the right panel binds to
/// the currently selected target's settings.
/// </summary>
public class TouchPageWallpaperSettingsViewModel : DialogViewModelBase<TouchButtonPage, DialogResult>
{
    public enum WallpaperTarget { Main, Left, Right }

    // Asset sub-folder for page wallpapers — kept in sync with WallpaperAssetMigrator.
    private const string WallpapersSubFolder = "wallpapers";

    private readonly IAssetService _assetService;
    private readonly bool _hasSideStrips;

    private TouchButtonPage _targetPage;

    // Snapshots of every slot for Cancel — restore the page's persisted state.
    private WallpaperSlot _mainSnapshot;
    private WallpaperSlot _leftSnapshot;
    private WallpaperSlot _rightSnapshot;

    private WallpaperTarget _selectedTarget = WallpaperTarget.Main;

    public ICommand SelectMainCommand { get; }
    public ICommand SelectLeftCommand { get; }
    public ICommand SelectRightCommand { get; }
    public ICommand SelectImageCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand MirrorToOtherSideCommand { get; }
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

    public TouchPageWallpaperSettingsViewModel(IAssetService assetService, IDeviceService deviceService)
    {
        _assetService = assetService;
        _hasSideStrips = deviceService?.Device?.HasSideStrips ?? false;

        SelectMainCommand = new RelayCommand(() => SelectedTarget = WallpaperTarget.Main);
        SelectLeftCommand = new RelayCommand(() => SelectedTarget = WallpaperTarget.Left);
        SelectRightCommand = new RelayCommand(() => SelectedTarget = WallpaperTarget.Right);
        SelectImageCommand = new AsyncRelayCommand(SelectImage);
        RemoveCommand = new RelayCommand(RemoveImage);
        ResetCommand = new RelayCommand(ResetAll);
        MirrorToOtherSideCommand = new RelayCommand(MirrorToOtherSide);
        ConfirmCommand = new RelayCommand(ConfirmDialog);
        CancelCommand = new RelayCommand(CancelDialog);
    }

    public override void Initialize(TouchButtonPage parameter)
    {
        _targetPage = parameter;

        // Snapshot every slot so Cancel can restore the persisted state.
        _mainSnapshot = _targetPage?.MainWallpaper?.Clone() ?? new WallpaperSlot();
        _leftSnapshot = _targetPage?.LeftWallpaper?.Clone() ?? new WallpaperSlot();
        _rightSnapshot = _targetPage?.RightWallpaper?.Clone() ?? new WallpaperSlot();

        OnPropertyChanged(nameof(PageName));
        OnPropertyChanged(nameof(HasSideStrips));
        NotifyTargetChanged();
        RefreshPreviews();
    }

    // ───────── Page / device ─────────

    public string PageName => _targetPage?.PageName ?? string.Empty;

    /// <summary>Only Razer-class devices expose the side displays.</summary>
    public bool HasSideStrips => _hasSideStrips;

    // ───────── Target selection ─────────

    public WallpaperTarget SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (_selectedTarget == value) return;
            _selectedTarget = value;
            NotifyTargetChanged();
        }
    }

    private WallpaperSlot ActiveSlot => _selectedTarget switch
    {
        WallpaperTarget.Left => _targetPage?.LeftWallpaper,
        WallpaperTarget.Right => _targetPage?.RightWallpaper,
        _ => _targetPage?.MainWallpaper,
    };

    public bool IsMainSelected => _selectedTarget == WallpaperTarget.Main;
    public bool IsLeftSelected => _selectedTarget == WallpaperTarget.Left;
    public bool IsRightSelected => _selectedTarget == WallpaperTarget.Right;

    /// <summary>True for the two side targets — gates "Mirror from other side".</summary>
    public bool IsSideSelected => _selectedTarget != WallpaperTarget.Main;

    public string ActiveTargetTitle => _selectedTarget switch
    {
        WallpaperTarget.Left => "Left Side Display",
        WallpaperTarget.Right => "Right Side Display",
        _ => "Main Wallpaper",
    };

    public string ActiveTargetSizeInfo => _selectedTarget switch
    {
        WallpaperTarget.Left => "Left Side Display: 60 × 270",
        WallpaperTarget.Right => "Right Side Display: 60 × 270",
        _ => "Main Wallpaper",
    };

    // Raise everything that depends on the active target.
    private void NotifyTargetChanged()
    {
        OnPropertyChanged(nameof(SelectedTarget));
        OnPropertyChanged(nameof(IsMainSelected));
        OnPropertyChanged(nameof(IsLeftSelected));
        OnPropertyChanged(nameof(IsRightSelected));
        OnPropertyChanged(nameof(IsSideSelected));
        OnPropertyChanged(nameof(ActiveTargetTitle));
        OnPropertyChanged(nameof(ActiveTargetSizeInfo));
        OnPropertyChanged(nameof(HasActiveImage));
        NotifyActiveSettingsChanged();
        OnPropertyChanged(nameof(ActivePreview));
    }

    // Raise the bound per-slot setting proxies (e.g. after a slot switch or a copy).
    private void NotifyActiveSettingsChanged()
    {
        OnPropertyChanged(nameof(WallpaperOpacity));
        OnPropertyChanged(nameof(SelectedWallpaperScalingOption));
        OnPropertyChanged(nameof(WallpaperScaling));
        OnPropertyChanged(nameof(WallpaperPositionX));
        OnPropertyChanged(nameof(WallpaperPositionY));
        OnPropertyChanged(nameof(WallpaperMirror));
    }

    // ───────── Active-slot setting proxies ─────────

    public bool HasActiveImage => ActiveSlot?.HasImage ?? false;

    public double WallpaperOpacity
    {
        get => ActiveSlot?.Opacity ?? 0;
        set { if (ActiveSlot != null) { ActiveSlot.Opacity = value; OnPropertyChanged(); RefreshPreviews(); } }
    }

    public BitmapHelper.ScalingOption SelectedWallpaperScalingOption
    {
        get => ActiveSlot?.ScalingOption ?? BitmapHelper.ScalingOption.Fit;
        set { if (ActiveSlot != null) { ActiveSlot.ScalingOption = value; OnPropertyChanged(); RefreshPreviews(); } }
    }

    public int WallpaperScaling
    {
        get => ActiveSlot?.Scaling ?? 100;
        set { if (ActiveSlot != null) { ActiveSlot.Scaling = value; OnPropertyChanged(); RefreshPreviews(); } }
    }

    public int WallpaperPositionX
    {
        get => ActiveSlot?.PositionX ?? 0;
        set { if (ActiveSlot != null) { ActiveSlot.PositionX = value; OnPropertyChanged(); RefreshPreviews(); } }
    }

    public int WallpaperPositionY
    {
        get => ActiveSlot?.PositionY ?? 0;
        set { if (ActiveSlot != null) { ActiveSlot.PositionY = value; OnPropertyChanged(); RefreshPreviews(); } }
    }

    public bool WallpaperMirror
    {
        get => ActiveSlot?.Mirror ?? false;
        set { if (ActiveSlot != null) { ActiveSlot.Mirror = value; OnPropertyChanged(); RefreshPreviews(); } }
    }

    // ───────── Previews ─────────

    public SKBitmap MainPreview =>
        BitmapHelper.GetOrBakeSlot(_targetPage?.MainWallpaper, BitmapHelper.PanelWidth, BitmapHelper.PanelHeight);

    public SKBitmap LeftPreview =>
        BitmapHelper.GetOrBakeSlot(_targetPage?.LeftWallpaper, 60, BitmapHelper.PanelHeight);

    public SKBitmap RightPreview =>
        BitmapHelper.GetOrBakeSlot(_targetPage?.RightWallpaper, 60, BitmapHelper.PanelHeight);

    public SKBitmap ActivePreview => _selectedTarget switch
    {
        WallpaperTarget.Left => LeftPreview,
        WallpaperTarget.Right => RightPreview,
        _ => MainPreview,
    };

    private void RefreshPreviews()
    {
        OnPropertyChanged(nameof(MainPreview));
        OnPropertyChanged(nameof(LeftPreview));
        OnPropertyChanged(nameof(RightPreview));
        OnPropertyChanged(nameof(ActivePreview));
        OnPropertyChanged(nameof(HasActiveImage));
    }

    // ───────── Commands ─────────

    private async Task SelectImage()
    {
        if (ActiveSlot == null) return;

        var result = await FileDialogHelper.OpenFileDialog();
        if (string.IsNullOrEmpty(result) || !File.Exists(result)) return;

        // Copy the original into the asset folder (content-hashed) under the dedicated
        // "wallpapers" sub-folder and reference it by relative path, like image layers.
        var relative = _assetService.Import(result, WallpapersSubFolder);
        if (string.IsNullOrEmpty(relative)) return;

        ActiveSlot.AssetPath = relative;
        NotifyActiveSettingsChanged();
        RefreshPreviews();
    }

    private void RemoveImage()
    {
        if (ActiveSlot == null) return;
        // Only drop the image — keep the slot's other settings.
        ActiveSlot.AssetPath = null;
        RefreshPreviews();
    }

    private void ResetAll()
    {
        _targetPage?.MainWallpaper?.Clear();
        _targetPage?.LeftWallpaper?.Clear();
        _targetPage?.RightWallpaper?.Clear();
        NotifyActiveSettingsChanged();
        RefreshPreviews();
    }

    private void MirrorToOtherSide()
    {
        if (_targetPage == null) return;

        // Copy the active side's settings onto the opposite side, then toggle the
        // target's Mirror so the copy is flipped relative to the source (a true
        // mirror image across to the other display).
        WallpaperSlot target = _selectedTarget switch
        {
            WallpaperTarget.Left => _targetPage.RightWallpaper,
            WallpaperTarget.Right => _targetPage.LeftWallpaper,
            _ => null, // not applicable for the main wallpaper
        };
        if (target == null) return;

        var source = _selectedTarget == WallpaperTarget.Left ? _targetPage.LeftWallpaper : _targetPage.RightWallpaper;
        target.CopyFrom(source);
        target.Mirror = !target.Mirror;

        RefreshPreviews();
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
            // Restore every slot from its snapshot → the live device redraw reverts too.
            _targetPage.MainWallpaper.CopyFrom(_mainSnapshot);
            _targetPage.LeftWallpaper.CopyFrom(_leftSnapshot);
            _targetPage.RightWallpaper.CopyFrom(_rightSnapshot);
        }
        Cancel();
        CloseRequested?.Invoke();
    }
}
