using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace LoupixDeck.Models;

/// <summary>
/// One app-switching rule: when the foreground window matches
/// <see cref="ProcessName"/> (and optionally contains <see cref="TitleContains"/>),
/// the deck switches to <see cref="TouchPageIndex"/> (and optionally
/// <see cref="RotaryPageIndex"/>). Indices are 0-based, matching <c>PageManager</c>.
/// INotifyPropertyChanged so the settings editor binds live to the shared config.
/// </summary>
public sealed class AppPageBinding : INotifyPropertyChanged
{
    private string _processName = string.Empty;
    public string ProcessName
    {
        get => _processName;
        set { if (_processName == value) return; _processName = value; OnPropertyChanged(); }
    }

    private string _titleContains = string.Empty;
    public string TitleContains
    {
        get => _titleContains;
        set { if (_titleContains == value) return; _titleContains = value; OnPropertyChanged(); }
    }

    private int _touchPageIndex;
    public int TouchPageIndex
    {
        get => _touchPageIndex;
        set { if (_touchPageIndex == value) return; _touchPageIndex = value; OnPropertyChanged(); }
    }

    private int? _rotaryPageIndex;
    public int? RotaryPageIndex
    {
        get => _rotaryPageIndex;
        set
        {
            if (_rotaryPageIndex == value) return;
            _rotaryPageIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RotarySelectionIndex));
        }
    }

    /// <summary>
    /// ComboBox helper for the rotary selector: 0 = "(unchanged)", n = rotary page n-1.
    /// Maps to/from the nullable <see cref="RotaryPageIndex"/>. Not persisted.
    /// </summary>
    [JsonIgnore]
    public int RotarySelectionIndex
    {
        get => _rotaryPageIndex is { } idx ? idx + 1 : 0;
        set => RotaryPageIndex = value <= 0 ? null : value - 1;
    }

    public event PropertyChangedEventHandler PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
