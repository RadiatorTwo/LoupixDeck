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
    private string _assetPath;
    private int _scaling = 100;
    private int _positionX;
    private int _positionY;
    private BitmapHelper.ScalingOption _scalingOption = BitmapHelper.ScalingOption.Fit;
    private double _opacity;
    private bool _mirror;
    private SKBitmap _baked;

    /// <summary>
    /// Relative path of the original image inside the asset folder
    /// (e.g. "assets/wallpapers/abc123.png"), or null when this slot has no image.
    /// </summary>
    public string AssetPath
    {
        get => _assetPath;
        set
        {
            if (_assetPath == value) return;
            _assetPath = value;
            Invalidate();
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasImage));
        }
    }

    public int Scaling
    {
        get => _scaling;
        set
        {
            if (_scaling == value) return;
            _scaling = value;
            Invalidate();
            OnPropertyChanged();
        }
    }

    public int PositionX
    {
        get => _positionX;
        set
        {
            if (_positionX == value) return;
            _positionX = value;
            Invalidate();
            OnPropertyChanged();
        }
    }

    public int PositionY
    {
        get => _positionY;
        set
        {
            if (_positionY == value) return;
            _positionY = value;
            Invalidate();
            OnPropertyChanged();
        }
    }

    public BitmapHelper.ScalingOption ScalingOption
    {
        get => _scalingOption;
        set
        {
            if (_scalingOption == value) return;
            _scalingOption = value;
            Invalidate();
            OnPropertyChanged();
        }
    }

    /// <summary>Horizontally flips the baked image.</summary>
    public bool Mirror
    {
        get => _mirror;
        set
        {
            if (_mirror == value) return;
            _mirror = value;
            Invalidate();
            OnPropertyChanged();
        }
    }

    /// <summary>Black dim overlay (0..1) drawn on top of the wallpaper.</summary>
    public double Opacity
    {
        get => _opacity;
        set
        {
            if (Math.Abs(_opacity - value) <= 0.0001) return;
            _opacity = value;
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
    public bool HasImage => !string.IsNullOrWhiteSpace(_assetPath);

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
        _assetPath = _assetPath,
        _scaling = _scaling,
        _positionX = _positionX,
        _positionY = _positionY,
        _scalingOption = _scalingOption,
        _opacity = _opacity,
        _mirror = _mirror,
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
