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
    /// Cached baked bitmap (480×270 for main, 60×270 for sides). NOT serialized —
    /// computed lazily via <see cref="BitmapHelper.GetOrBakeSlot"/>.
    /// </summary>
    [JsonIgnore]
    public SKBitmap Baked { get; set; }

    [JsonIgnore]
    public bool HasImage => !string.IsNullOrWhiteSpace(AssetPath);

    /// <summary>
    /// Raised whenever a property that affects the rendered result changes, so the
    /// owning <see cref="TouchButtonPage"/> can ask the controller to repaint.
    /// </summary>
    public event EventHandler Changed;

    private void Invalidate()
    {
        Baked = null;
        RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Deep copy of the parameters (not the baked cache).</summary>
    public WallpaperSlot Clone() => new()
    {
        AssetPath = AssetPath,
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
        Scaling = 100;
        PositionX = 0;
        PositionY = 0;
        ScalingOption = BitmapHelper.ScalingOption.Fit;
        Opacity = 0;
        Mirror = false;
    }
}
