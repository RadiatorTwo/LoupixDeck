using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LoupixDeck.Models;

/// <summary>
/// This data model holds all configuration settings,
/// which are loaded and saved via JSON.
/// </summary>
public class LoupedeckConfig : INotifyPropertyChanged
{
    private int _currentTouchPageIndex;
    private int _currentRotaryPageIndex;
    private int _brightness = 1;

    public string DevicePort { get; set; }
    public int DeviceBaudrate { get; set; }

    public ObservableCollection<RotaryButtonPage> RotaryButtonPages { get; set; } =
        new ObservableCollection<RotaryButtonPage>();

    public ObservableCollection<TouchButtonPage> TouchButtonPages { get; set; } =
        new ObservableCollection<TouchButtonPage>();

    public SimpleButton[] SimpleButtons { get; set; }
    public TouchButtonPage CurrentTouchButtonPage { get; set; }
    public RotaryButtonPage CurrentRotaryButtonPage { get; set; }

    public int CurrentTouchPageIndex
    {
        get => _currentTouchPageIndex;
        set
        {
            if (_currentTouchPageIndex != value)
            {
                _currentTouchPageIndex = value;
                OnPropertyChanged();
            }
        }
    }

    public int CurrentRotaryPageIndex
    {
        get => _currentRotaryPageIndex;
        set
        {
            if (_currentRotaryPageIndex != value)
            {
                _currentRotaryPageIndex = value;
                OnPropertyChanged();
            }
        }
    }

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

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}