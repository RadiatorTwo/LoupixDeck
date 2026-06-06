using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

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
    private string _name;
    private bool _selected;

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
    public string PageName => string.IsNullOrWhiteSpace(_name) ? $"Rotary Page: {Page}" : _name;

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

    public ObservableCollection<RotaryButton> RotaryButtons { get; set; }

    // Pre/Post-command wraps applied per input type when a button on this page fires.
    public CommandWrap SimpleButtonWrap { get; set; } = new();
    public CommandWrap KnobLeftWrap { get; set; } = new();
    public CommandWrap KnobRightWrap { get; set; } = new();
    public CommandWrap KnobPressWrap { get; set; } = new();

    public event PropertyChangedEventHandler PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}