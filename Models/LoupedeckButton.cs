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
            _command = value;
            OnPropertyChanged(nameof(Command));
        }
    }

    public event EventHandler ItemChanged;
    public event PropertyChangedEventHandler PropertyChanged;

    public void Refresh()
    {
        ItemChanged?.Invoke(this, EventArgs.Empty);
    }
    
    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}