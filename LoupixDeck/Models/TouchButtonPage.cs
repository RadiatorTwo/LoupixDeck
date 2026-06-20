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

        MainWallpaper = new WallpaperSlot();
        LeftWallpaper = new WallpaperSlot();
        RightWallpaper = new WallpaperSlot();
    }

    private int _page;
    private string _name;
    private bool _selected;
    private WallpaperSlot _mainWallpaper;
    private WallpaperSlot _leftWallpaper;
    private WallpaperSlot _rightWallpaper;

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
    /// Main 480×270 wallpaper. Always non-null; an empty slot (no
    /// <see cref="WallpaperSlot.AssetPath"/>) means "no wallpaper".
    /// </summary>
    public WallpaperSlot MainWallpaper
    {
        get => _mainWallpaper;
        set
        {
            if (ReferenceEquals(_mainWallpaper, value)) return;
            if (_mainWallpaper != null) _mainWallpaper.Changed -= OnWallpaperSlotChanged;
            _mainWallpaper = value;
            if (_mainWallpaper != null) _mainWallpaper.Changed += OnWallpaperSlotChanged;
            OnPropertyChanged();
            OnWallpaperSlotChanged(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Optional wallpaper for the left Razer side display (60×270). When set it
    /// overdraws the main wallpaper's left region; empty falls back to the main.
    /// </summary>
    public WallpaperSlot LeftWallpaper
    {
        get => _leftWallpaper;
        set
        {
            if (ReferenceEquals(_leftWallpaper, value)) return;
            if (_leftWallpaper != null) _leftWallpaper.Changed -= OnWallpaperSlotChanged;
            _leftWallpaper = value;
            if (_leftWallpaper != null) _leftWallpaper.Changed += OnWallpaperSlotChanged;
            OnPropertyChanged();
            OnWallpaperSlotChanged(this, EventArgs.Empty);
        }
    }

    /// <summary>Optional wallpaper for the right Razer side display (60×270).</summary>
    public WallpaperSlot RightWallpaper
    {
        get => _rightWallpaper;
        set
        {
            if (ReferenceEquals(_rightWallpaper, value)) return;
            if (_rightWallpaper != null) _rightWallpaper.Changed -= OnWallpaperSlotChanged;
            _rightWallpaper = value;
            if (_rightWallpaper != null) _rightWallpaper.Changed += OnWallpaperSlotChanged;
            OnPropertyChanged();
            OnWallpaperSlotChanged(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Baked main wallpaper, used for thumbnails/previews (settings list). Read-only;
    /// computed on demand from <see cref="MainWallpaper"/>. Returns null when unset.
    /// </summary>
    [JsonIgnore]
    public SKBitmap Wallpaper =>
        BitmapHelper.GetOrBakeSlot(MainWallpaper, BitmapHelper.PanelWidth, BitmapHelper.PanelHeight);

    /// <summary>Change signal (no value) raised whenever any wallpaper slot's
    /// rendered result changes, so the controller repaints. JsonIgnore — purely a
    /// notification, never persisted.</summary>
    [JsonIgnore]
    public bool WallpaperInvalidated => false;

    private void OnWallpaperSlotChanged(object sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Wallpaper));
        OnPropertyChanged(nameof(WallpaperInvalidated));
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