using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Models;
using LoupixDeck.Registry;
using LoupixDeck.Services;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;
using SkiaSharp;
// Utils.RelayCommand executes via Task.Run (background thread) — that would
// raise CloseRequested off the UI thread and crash Window.Close(). Use the
// CommunityToolkit synchronous RelayCommand for dialog buttons.

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
    private TouchButtonPage _targetPage;

    // Snapshots of every slot for Cancel — restore the page's persisted state.
    private WallpaperSlot _mainSnapshot;
    private WallpaperSlot _leftSnapshot;
    private WallpaperSlot _rightSnapshot;

    public IRelayCommand SelectMainCommand => field ??= Relay.Create(() => SelectedTarget = WallpaperTarget.Main);
    public IRelayCommand SelectLeftCommand => field ??= Relay.Create(() => SelectedTarget = WallpaperTarget.Left);
    public IRelayCommand SelectRightCommand => field ??= Relay.Create(() => SelectedTarget = WallpaperTarget.Right);
    public IAsyncRelayCommand SelectMediaCommand => field ??= Relay.Create(SelectMedia);
    public IRelayCommand RemoveCommand => field ??= Relay.Create(RemoveMedia);
    public IRelayCommand ResetCommand => field ??= Relay.Create(ResetAll);
    public IRelayCommand MirrorToOtherSideCommand => field ??= Relay.Create(MirrorToOtherSide);
    public IRelayCommand ConfirmCommand => field ??= Relay.Create(ConfirmDialog);
    public IRelayCommand CancelCommand => field ??= Relay.Create(CancelDialog);

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

    /// <summary>
    /// The scaling options a clip can actually honour. None/Tile/Center have no video meaning, so
    /// offering them would be another control that promises an effect it cannot deliver.
    /// </summary>
    public ObservableCollection<BitmapHelper.ScalingOption> VideoScalingOptions { get; } =
    [
        BitmapHelper.ScalingOption.Fit,
        BitmapHelper.ScalingOption.Fill,
        BitmapHelper.ScalingOption.Stretch,
    ];

    private readonly DeviceGeometry _geometry;

    public TouchPageWallpaperSettingsViewModel(IAssetService assetService, IDeviceService deviceService,
        DeviceGeometry geometry)
    {
        _assetService = assetService;
        _geometry = geometry ?? DeviceGeometry.Default;
        HasSideStrips = deviceService?.Device?.HasSideStrips ?? false;

        // Probe off the UI thread — the first probe can block briefly. Drives the "ffmpeg missing"
        // hint, exactly as the screensaver settings do.
        Task.Run(() =>
        {
            var available = FfmpegDetector.IsAvailable();
            Dispatcher.UIThread.Post(() =>
            {
                _ffmpegAvailable = available;
                OnPropertyChanged(nameof(ShowFfmpegHint));
            });
        });
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
    public bool HasSideStrips { get; }

    // ───────── Target selection ─────────

    public WallpaperTarget SelectedTarget
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            NotifyTargetChanged();
        }
    } = WallpaperTarget.Main;

    private WallpaperSlot ActiveSlot => SelectedTarget switch
    {
        WallpaperTarget.Left => _targetPage?.LeftWallpaper,
        WallpaperTarget.Right => _targetPage?.RightWallpaper,
        _ => _targetPage?.MainWallpaper,
    };

    public bool IsMainSelected => SelectedTarget == WallpaperTarget.Main;
    public bool IsLeftSelected => SelectedTarget == WallpaperTarget.Left;
    public bool IsRightSelected => SelectedTarget == WallpaperTarget.Right;

    /// <summary>True for the two side targets — gates "Mirror from other side".</summary>
    public bool IsSideSelected => SelectedTarget != WallpaperTarget.Main;

    public string ActiveTargetTitle => SelectedTarget switch
    {
        WallpaperTarget.Left => "Left Side Display",
        WallpaperTarget.Right => "Right Side Display",
        _ => "Main Wallpaper",
    };

    public string ActiveTargetSizeInfo => SelectedTarget switch
    {
        WallpaperTarget.Left => $"Left Side Display: {_geometry.StripWidth} × {_geometry.PanelHeight}",
        WallpaperTarget.Right => $"Right Side Display: {_geometry.StripWidth} × {_geometry.PanelHeight}",
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
        OnPropertyChanged(nameof(SupportsVideo));
        OnPropertyChanged(nameof(ShowFfmpegHint));
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
        OnPropertyChanged(nameof(HasActiveVideo));
        OnPropertyChanged(nameof(HasActiveContent));
        OnPropertyChanged(nameof(ActiveContentKind));
        OnPropertyChanged(nameof(SelectedMediaName));
        OnPropertyChanged(nameof(ShowImageAdjustments));
        OnPropertyChanged(nameof(VideoFps));
    }

    // ───────── Active-slot setting proxies ─────────

    public bool HasActiveImage => ActiveSlot?.HasImage ?? false;

    /// <summary>Whether the slot holds anything at all — what "Remove" acts on.</summary>
    public bool HasActiveContent => HasActiveImage || HasActiveVideo;

    /// <summary>Names what the slot is holding, so one picker still says which kind it picked.</summary>
    public string ActiveContentKind => ActiveSlot switch
    {
        { HasVideo: true } => "Video clip",
        { HasImage: true } => "Image",
        _ => "Empty",
    };

    /// <summary>File name of whatever the slot holds; the image's own name is not stored, so an
    /// image falls back to the hashed asset file name it was imported as.</summary>
    public string SelectedMediaName
    {
        get
        {
            var slot = ActiveSlot;
            if (slot == null) return "Nothing selected";

            if (slot.HasVideo)
                return string.IsNullOrWhiteSpace(slot.VideoName)
                    ? Path.GetFileName(slot.VideoPath)
                    : slot.VideoName;

            return slot.HasImage ? Path.GetFileName(slot.AssetPath) : "Nothing selected";
        }
    }

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

    // ───────── Video clip (main target only) ─────────

    /// <summary>
    /// Only the main slot plays a clip: it covers the whole panel, side strips included, so a
    /// per-side clip has nothing left to play on. A side slot's still image still wins over the
    /// video for its own column.
    /// </summary>
    public bool SupportsVideo => IsMainSelected;

    public bool HasActiveVideo => ActiveSlot?.HasVideo ?? false;

    /// <summary>
    /// Whether the scaling/position/mirror controls apply. They do not while a clip is selected —
    /// ffmpeg scales it to the panel and the overlay reads only <see cref="WallpaperOpacity"/> from
    /// the slot — so the dialog hides them rather than offering settings with no effect. The stored
    /// values are untouched and come back with the still image.
    /// </summary>
    public bool ShowImageAdjustments => !HasActiveVideo;

    /// <summary>Whether ffmpeg was found on PATH. Assumed present until the probe answers, so the
    /// hint never flashes on a machine that has it.</summary>
    private bool _ffmpegAvailable = true;

    /// <summary>Shown only where a clip can actually be selected.</summary>
    public bool ShowFfmpegHint => !_ffmpegAvailable && SupportsVideo;

    public int VideoFps
    {
        get => ActiveSlot?.VideoFps ?? 30;
        set { if (ActiveSlot != null) { ActiveSlot.VideoFps = value; OnPropertyChanged(); } }
    }

    // ───────── Previews ─────────

    public SKBitmap MainPreview
    {
        get
        {
            var slot = _targetPage?.MainWallpaper;
            if (slot is { HasVideo: true })
                return GetPosterFrame(slot.VideoPath);

            return BitmapHelper.GetOrBakeSlot(slot, _geometry.PanelWidth, _geometry.PanelHeight);
        }
    }

    // The poster frame costs an ffmpeg call, and the preview properties are re-read on every slider
    // move, so keep the last one. Only one clip can be selected at a time, so a single-entry cache
    // is the whole requirement.
    private string _posterPath;
    private SKBitmap _poster;

    private SKBitmap GetPosterFrame(string path)
    {
        if (string.Equals(path, _posterPath, StringComparison.Ordinal)) return _poster;

        _poster?.Dispose();
        _poster = VideoPosterFrame.Extract(path, _geometry.PanelWidth, _geometry.PanelHeight);
        _posterPath = path;
        return _poster;
    }

    public SKBitmap LeftPreview =>
        BitmapHelper.GetOrBakeSlot(_targetPage?.LeftWallpaper, _geometry.StripWidth, _geometry.PanelHeight);

    public SKBitmap RightPreview =>
        BitmapHelper.GetOrBakeSlot(_targetPage?.RightWallpaper, _geometry.StripWidth, _geometry.PanelHeight);

    public SKBitmap ActivePreview => SelectedTarget switch
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

    /// <summary>
    /// One picker for both kinds of wallpaper, because a slot shows one or the other and never
    /// both. The choice is classified by extension and lands in the matching fields; whatever was
    /// there before is cleared, so the dialog can never leave a slot holding a clip and an image at
    /// once and leave the user guessing which one wins.
    /// </summary>
    private async Task SelectMedia()
    {
        if (ActiveSlot == null) return;

        var path = await FileDialogHelper.OpenWallpaperMediaDialog();
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        if (IsVideoFile(path))
        {
            if (!SupportsVideo)
            {
                // A side display is covered by the main slot's clip and never plays one of its own.
                Console.WriteLine("[Wallpaper] a side display cannot play a clip — selection ignored.");
                return;
            }

            // Referenced in place, never imported: a clip may be arbitrarily large and ffmpeg reads
            // it straight from disk, so a copy into the asset store only wastes space. Same rule as
            // the screensaver clip.
            ActiveSlot.VideoPath = path;
            ActiveSlot.VideoName = Path.GetFileName(path);
            ActiveSlot.AssetPath = null;

            // The slot may still carry an image-only fit (Tile, say). Leaving it would show an
            // empty combo and silently fall back to Stretch, so settle on Fit — the one choice
            // that never distorts the clip.
            if (!VideoScalingOptions.Contains(ActiveSlot.ScalingOption))
                ActiveSlot.ScalingOption = BitmapHelper.ScalingOption.Fit;
        }
        else
        {
            // Copy the original into the asset folder (content-hashed) under the dedicated
            // "wallpapers" sub-folder and reference it by relative path, like image layers.
            var relative = _assetService.Import(path, WallpapersSubFolder);
            if (string.IsNullOrEmpty(relative)) return;

            ActiveSlot.AssetPath = relative;
            ActiveSlot.VideoPath = null;
            ActiveSlot.VideoName = null;
        }

        NotifyActiveSettingsChanged();
        RefreshPreviews();
    }

    /// <summary>Empties the slot's content — image or clip — and keeps its other settings.</summary>
    private void RemoveMedia()
    {
        if (ActiveSlot == null) return;

        ActiveSlot.AssetPath = null;
        ActiveSlot.VideoPath = null;
        ActiveSlot.VideoName = null;
        NotifyActiveSettingsChanged();
        RefreshPreviews();
    }

    /// <summary>Extensions the wallpaper picker treats as a clip rather than a still image.</summary>
    private static readonly string[] VideoExtensions =
        [".mp4", ".webm", ".mov", ".mkv", ".m4v", ".avi"];

    private static bool IsVideoFile(string path) =>
        VideoExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

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
        WallpaperSlot target = SelectedTarget switch
        {
            WallpaperTarget.Left => _targetPage.RightWallpaper,
            WallpaperTarget.Right => _targetPage.LeftWallpaper,
            _ => null, // not applicable for the main wallpaper
        };
        if (target == null) return;

        var source = SelectedTarget == WallpaperTarget.Left ? _targetPage.LeftWallpaper : _targetPage.RightWallpaper;
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
