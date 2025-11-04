using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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
    private bool _selected;
    private SKBitmap _wallpaper;
    private double _wallpaperOpacity;

    public string PageName => $"Page: {Page}";

    public int Page
    {
        get => _page;
        set
        {
            if (_page == value) return;
            _page = value;
            OnPropertyChanged();
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
        
    public event PropertyChangedEventHandler PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}