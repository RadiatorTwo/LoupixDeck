using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using SkiaSharp;

namespace LoupixDeck.Models;

/// <summary>
/// This data model holds all configuration settings,
/// which are loaded and saved via JSON.
/// </summary>
public class LoupedeckConfig : INotifyPropertyChanged
{
    private int _currentRotaryPageIndex = -1;
    private int _currentTouchPageIndex = -1;

    private int _brightness = 100;

    public string DevicePort { get; set; }
    public int DeviceBaudrate { get; set; }

    public SimpleButton[] SimpleButtons { get; set; }

    public ObservableCollection<RotaryButtonPage> RotaryButtonPages { get; set; } = [];

    [JsonIgnore]
    public int CurrentRotaryPageIndex
    {
        get => _currentRotaryPageIndex;
        set
        {
            if (_currentRotaryPageIndex != value)
            {
                _currentRotaryPageIndex = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentRotaryButtonPage));
            }
        }
    }

    [JsonIgnore]
    public RotaryButtonPage CurrentRotaryButtonPage =>
        (RotaryButtonPages != null &&
         _currentRotaryPageIndex >= 0 &&
         _currentRotaryPageIndex < RotaryButtonPages.Count)
            ? RotaryButtonPages[_currentRotaryPageIndex]
            : null;

    public ObservableCollection<TouchButtonPage> TouchButtonPages { get; set; } = [];

    [JsonIgnore]
    public int CurrentTouchPageIndex
    {
        get => _currentTouchPageIndex;
        set
        {
            if (_currentTouchPageIndex != value)
            {
                _currentTouchPageIndex = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentTouchButtonPage));
            }
        }
    }

    [JsonIgnore]
    public TouchButtonPage CurrentTouchButtonPage =>
        (TouchButtonPages != null &&
         _currentTouchPageIndex >= 0 &&
         _currentTouchPageIndex < TouchButtonPages.Count)
            ? TouchButtonPages[_currentTouchPageIndex]
            : null;

    public int Brightness
    {
        get => _brightness;
        set
        {
            if (_brightness == value) return;
            _brightness = value;
            OnPropertyChanged();
        }
    }

    private SKBitmap _wallpaper;

    [JsonConverter(typeof(SKBitmapBase64Converter))]
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

    private double _wallpaperOpacity;

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

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}