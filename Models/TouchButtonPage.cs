using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LoupixDeck.Utils;
using Newtonsoft.Json;
using SkiaSharp;

namespace LoupixDeck.Models;

public class TouchButtonPage : INotifyPropertyChanged
{
    public TouchButtonPage(int pageSize)
    {
        TouchButtons = new ObservableCollection<TouchButton>();

        for (var i = 0; i < pageSize; i++)
        {
            var newButton = new TouchButton(i);
            TouchButtons.Add(newButton);
        }
    }

    private int _page;
    private string _name;
    private bool _selected;
    private SKBitmap _wallpaper;
    private double _wallpaperOpacity;
    private string _wallpaperAssetPath;
    private int _wallpaperScaling = 100;
    private int _wallpaperPositionX;
    private int _wallpaperPositionY;
    private BitmapHelper.ScalingOption _wallpaperScalingOption = BitmapHelper.ScalingOption.Fit;

    /// <summary>
    /// Optional user-assigned page name. Persisted; when empty the page falls back
    /// to its number, so configs written before naming existed load unchanged.
    /// </summary>
    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PageName));
        }
    }

    [JsonIgnore]
    public string PageName => string.IsNullOrWhiteSpace(_name) ? $"Page: {Page}" : _name;

    public int Page
    {
        get => _page;
        set
        {
            if (_page == value) return;
            _page = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PageName));
        }
    }

    [JsonIgnore]
    public bool Selected
    {
        get => _selected;
        set
        {
            if (value == _selected) return;
            _selected = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Relative path of the wallpaper's <b>original</b> image inside the asset
    /// folder (e.g. "assets/abc123.png"), or null when no wallpaper is set. The
    /// scaled 480×270 bitmap actually drawn is computed on demand from this plus
    /// the scaling parameters below — see <see cref="Wallpaper"/>.
    /// </summary>
    public string WallpaperAssetPath
    {
        get => _wallpaperAssetPath;
        set
        {
            if (_wallpaperAssetPath == value) return;
            _wallpaperAssetPath = value;
            InvalidateWallpaper();
            OnPropertyChanged();
        }
    }

    public int WallpaperScaling
    {
        get => _wallpaperScaling;
        set
        {
            if (_wallpaperScaling == value) return;
            _wallpaperScaling = value;
            InvalidateWallpaper();
            OnPropertyChanged();
        }
    }

    public int WallpaperPositionX
    {
        get => _wallpaperPositionX;
        set
        {
            if (_wallpaperPositionX == value) return;
            _wallpaperPositionX = value;
            InvalidateWallpaper();
            OnPropertyChanged();
        }
    }

    public int WallpaperPositionY
    {
        get => _wallpaperPositionY;
        set
        {
            if (_wallpaperPositionY == value) return;
            _wallpaperPositionY = value;
            InvalidateWallpaper();
            OnPropertyChanged();
        }
    }

    public BitmapHelper.ScalingOption WallpaperScalingOption
    {
        get => _wallpaperScalingOption;
        set
        {
            if (_wallpaperScalingOption == value) return;
            _wallpaperScalingOption = value;
            InvalidateWallpaper();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Cached 480×270 wallpaper bitmap baked from the original asset and the
    /// scaling parameters. NOT serialized — loaded/computed lazily via
    /// <see cref="BitmapHelper.GetOrBakeWallpaper"/>. Mirrors the
    /// <c>ImageLayer.CachedImage</c> pattern.
    /// </summary>
    [JsonIgnore]
    public SKBitmap Wallpaper
    {
        get => _wallpaper;
        set
        {
            if (Equals(value, _wallpaper)) return;
            _wallpaper = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Drops the cached baked wallpaper so it is re-computed on the next
    /// render; raises <see cref="Wallpaper"/> so the controller repaints.</summary>
    private void InvalidateWallpaper()
    {
        _wallpaper = null;
        OnPropertyChanged(nameof(Wallpaper));
    }

    public double WallpaperOpacity
    {
        get => _wallpaperOpacity;
        set
        {
            if (!(Math.Abs(_wallpaperOpacity - value) > 0.0001)) return;
            _wallpaperOpacity = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<TouchButton> TouchButtons { get; set; }

    /// <summary>Pre/Post-command wrap applied to every touch button on this page.</summary>
    public CommandWrap TouchButtonWrap { get; set; } = new();

    public event PropertyChangedEventHandler PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}