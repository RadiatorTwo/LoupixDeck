using CommunityToolkit.Mvvm.ComponentModel;
using LoupixDeck.Utils;
using Newtonsoft.Json;
using SkiaSharp;

namespace LoupixDeck.Models;

/// <summary>
/// One wallpaper target — either the main 480×270 panel or one of the Razer side
/// displays (60×270). Holds the persistent reference to the original image in the
/// asset folder plus its scaling / position / opacity / mirror parameters; the
/// scaled bitmap actually drawn is baked on demand from these and cached in
/// <see cref="Baked"/> (not serialized). Mirrors the per-page wallpaper model that
/// previously lived flat on <see cref="TouchButtonPage"/>.
///
/// A slot may instead reference a video clip (<see cref="VideoPath"/>), which the main slot plays
/// behind the keys. The still image and its parameters stay untouched while a clip is set, so
/// clearing the clip restores exactly the previous wallpaper.
/// </summary>
[ObservableObject]
public partial class WallpaperSlot
{

    /// <summary>
    /// Relative path of the original image inside the asset folder
    /// (e.g. "assets/wallpapers/abc123.png"), or null when this slot has no image.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    public partial string AssetPath { get; set; }
    partial void OnAssetPathChanged(string value) => Invalidate();

    /// <summary>
    /// Absolute path of a video clip to play in this slot instead of the still image, or null.
    /// Deliberately a path reference rather than an imported asset (mirroring
    /// <c>LoupedeckConfig.ScreensaverVideoPath</c>): a clip may be arbitrarily large, and copying
    /// it into the content-addressed asset store would duplicate it per slot.
    ///
    /// Absent from a config saved before this existed, so an old file loads as a still-image slot
    /// and behaves exactly as it did.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVideo))]
    public partial string VideoPath { get; set; }
    partial void OnVideoPathChanged(string value) => RaiseChanged();

    /// <summary>Display name of the chosen clip, for the settings UI.</summary>
    [ObservableProperty]
    public partial string VideoName { get; set; }

    /// <summary>
    /// Frame rate to play the clip at. The scheduler clamps this to its global limit, and the real
    /// ceiling is the serial panel write (~27 ms per frame on a 480×270 panel), so values far above
    /// 30 buy nothing.
    /// </summary>
    [ObservableProperty]
    public partial int VideoFps { get; set; } = 30;
    partial void OnVideoFpsChanged(int value) => RaiseChanged();

    [ObservableProperty]
    public partial int Scaling { get; set; } = 100;
    partial void OnScalingChanged(int value) => Invalidate();

    [ObservableProperty]
    public partial int PositionX { get; set; }
    partial void OnPositionXChanged(int value) => Invalidate();

    [ObservableProperty]
    public partial int PositionY { get; set; }
    partial void OnPositionYChanged(int value) => Invalidate();

    [ObservableProperty]
    public partial BitmapHelper.ScalingOption ScalingOption { get; set; } = BitmapHelper.ScalingOption.Fit;
    partial void OnScalingOptionChanged(BitmapHelper.ScalingOption value) => Invalidate();

    /// <summary>Horizontally flips the baked image.</summary>
    [ObservableProperty]
    public partial bool Mirror { get; set; }
    partial void OnMirrorChanged(bool value) => Invalidate();

    /// <summary>Black dim overlay (0..1) drawn on top of the wallpaper.</summary>
    public double Opacity
    {
        get;
        set
        {
            if (Math.Abs(field - value) <= 0.0001) return;
            field = value;
            // Opacity is applied at draw time, not baked in — no bake invalidation,
            // but the rendered result still changes.
            RaiseChanged();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Cached baked bitmap, sized to the surface it was baked for (the device's panel for
    /// the main slot, a side strip for the others). NOT serialized — computed lazily via
    /// <see cref="BitmapHelper.GetOrBakeSlot"/>, which re-bakes when a different size is
    /// asked for (see <see cref="BakedSize"/>).
    /// </summary>
    [JsonIgnore]
    public SKBitmap Baked { get; set; }

    /// <summary>
    /// Surface size <see cref="Baked"/> was produced for. The cache must be keyed on this:
    /// devices do not share one panel size, so a slot baked once for one panel would
    /// otherwise be handed straight back to a differently sized one, losing rows and
    /// sampling every key from the wrong place.
    /// </summary>
    [JsonIgnore]
    public (int Width, int Height) BakedSize { get; set; }

    [JsonIgnore]
    public bool HasImage => !string.IsNullOrWhiteSpace(AssetPath);

    /// <summary>
    /// Whether this slot plays a clip. It wins over <see cref="HasImage"/> when both are set; the
    /// still image is kept rather than cleared, so removing the clip restores it — and it is what
    /// the slot falls back to when ffmpeg is unavailable.
    /// </summary>
    [JsonIgnore]
    public bool HasVideo => !string.IsNullOrWhiteSpace(VideoPath);

    /// <summary>
    /// Raised whenever a property that affects the rendered result changes, so the
    /// owning <see cref="TouchButtonPage"/> can ask the controller to repaint.
    /// </summary>
    public event EventHandler Changed;

    private void Invalidate()
    {
        Baked = null;
        BakedSize = default;
        RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Deep copy of the parameters (not the baked cache).</summary>
    public WallpaperSlot Clone() => new()
    {
        AssetPath = AssetPath,
        VideoPath = VideoPath,
        VideoName = VideoName,
        VideoFps = VideoFps,
        Scaling = Scaling,
        PositionX = PositionX,
        PositionY = PositionY,
        ScalingOption = ScalingOption,
        Opacity = Opacity,
        Mirror = Mirror,
    };

    /// <summary>Copies all parameters (and the image reference) from another slot.</summary>
    public void CopyFrom(WallpaperSlot other)
    {
        if (other == null) return;
        AssetPath = other.AssetPath;
        VideoPath = other.VideoPath;
        VideoName = other.VideoName;
        VideoFps = other.VideoFps;
        Scaling = other.Scaling;
        PositionX = other.PositionX;
        PositionY = other.PositionY;
        ScalingOption = other.ScalingOption;
        Opacity = other.Opacity;
        Mirror = other.Mirror;
    }

    /// <summary>Resets the slot to its empty default (no image).</summary>
    public void Clear()
    {
        AssetPath = null;
        VideoPath = null;
        VideoName = null;
        VideoFps = 30;
        Scaling = 100;
        PositionX = 0;
        PositionY = 0;
        ScalingOption = BitmapHelper.ScalingOption.Fit;
        Opacity = 0;
        Mirror = false;
    }
}
