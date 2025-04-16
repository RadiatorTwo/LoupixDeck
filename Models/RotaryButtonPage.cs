using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;

namespace LoupixDeck.Models;

public class RotaryButtonPage : INotifyPropertyChanged
{
    public RotaryButtonPage(int pageSize)
    {
        RotaryButtons = new ObservableCollection<RotaryButton>();

        for (var i = 0; i < pageSize; i++)
        {
            var newButton = new RotaryButton(i, string.Empty, string.Empty);
            RotaryButtons.Add(newButton);
        }
    }
        
    private int _page;

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
    
    public ObservableCollection<RotaryButton> RotaryButtons { get; set; }
    
    public event PropertyChangedEventHandler PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}