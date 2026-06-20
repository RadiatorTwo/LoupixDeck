using System.ComponentModel;

namespace LoupixDeck.Models;

public class LoupedeckButton : INotifyPropertyChanged
{
    private string _command;
    public string Command
    {
        get => _command;
        set
        {
            if (_command == value) return;
            _command = value;
            OnPropertyChanged(nameof(Command));
        }
    }

    public bool IgnoreRefresh {
        get;
        set;
    }

    private bool _enableWhenOff;

    /// <summary>
    /// When true, this button's command still runs while the device is in the
    /// OFF state (manual toggle or auto-OFF during system suspend). Used e.g.
    /// for a "wake the device" button that needs to function while everything
    /// else is muted.
    /// </summary>
    public virtual bool EnableWhenOff
    {
        get => _enableWhenOff;
        set
        {
            if (_enableWhenOff == value) return;
            _enableWhenOff = value;
            OnPropertyChanged(nameof(EnableWhenOff));
        }
    }

    public event EventHandler ItemChanged;
    public event PropertyChangedEventHandler PropertyChanged;

    public void Refresh()
    {
        if (IgnoreRefresh) return;
        ItemChanged?.Invoke(this, EventArgs.Empty);
    }
    
    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}