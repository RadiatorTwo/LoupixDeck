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
    /// Which dial column this page belongs to. Defaults to <see cref="RotarySide.Both"/>
    /// so configs written before the side split (and devices without side strips,
    /// e.g. the Live S) keep the single-column behaviour. The v3→v4 migration tags
    /// Razer pages <see cref="RotarySide.Left"/> / <see cref="RotarySide.Right"/>.
    /// </summary>
    public RotarySide Side { get; set; } = RotarySide.Both;

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

    /// <summary>
    /// Free-draw canvas for this page's side strip: a 60×270 layer surface (image/
    /// text/symbol) edited like a touch button, shown when the side's
    /// <see cref="StripMode"/> is <see cref="StripMode.FreeDraw"/>. Null/absent in
    /// older configs and in segmented mode; created on demand by the editor.
    /// </summary>
    public TouchButton StripCanvas { get; set; }

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