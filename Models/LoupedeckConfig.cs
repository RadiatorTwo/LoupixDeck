using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace LoupixDeck.Models;

/// <summary>
/// This data model holds all configuration settings,
/// which are loaded and saved via JSON.
/// </summary>
public class LoupedeckConfig : INotifyPropertyChanged
{
    private int _currentRotaryPageIndex = -1;
    private int _currentTouchPageIndex = -1;
    
    private int _brightness = 1;

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
    
    private Avalonia.Media.Imaging.Bitmap _wallpaper;
    
    public Avalonia.Media.Imaging.Bitmap Wallpaper
    {
        get => _wallpaper;
        set
        {
            if (Equals(value, _wallpaper)) return;
            _wallpaper = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}