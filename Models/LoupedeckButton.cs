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