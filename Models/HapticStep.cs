using System.ComponentModel;
using System.Runtime.CompilerServices;
using LoupixDeck.LoupedeckDevice;

namespace LoupixDeck.Models;

public class HapticStep : INotifyPropertyChanged
{
    private byte _effect = Constants.VibrationPattern.SharpClick;
    public byte Effect
    {
        get => _effect;
        set { if (_effect == value) return; _effect = value; OnPropertyChanged(); }
    }

    private byte _delay = 0x32;
    public byte Delay
    {
        get => _delay;
        set
        {
            var clamped = value < 0x04 ? (byte)0x04 : value;
            if (_delay == clamped) return;
            _delay = clamped;
            OnPropertyChanged();
        }
    }

    private byte _duration = 0x10;
    public byte Duration
    {
        get => _duration;
        set
        {
            var clamped = value < 0x02 ? (byte)0x02 : value;
            if (_duration == clamped) return;
            _duration = clamped;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
