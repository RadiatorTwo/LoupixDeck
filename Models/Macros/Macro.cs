using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LoupixDeck.Models.Macros;

/// <summary>
/// A named, user-defined macro: an ordered list of steps executed sequentially
/// by <c>System.Macro(Name)</c>. Persisted in macros.json (see <see cref="MacroSettings"/>).
/// </summary>
public class Macro : INotifyPropertyChanged
{
    private string _name = string.Empty;

    /// <summary>
    /// Unique macro name. Must not contain '(' ')' ',' or '&amp;' — those would break
    /// the command parser when the macro is invoked as System.Macro(Name).
    /// </summary>
    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<MacroStep> Steps { get; set; } = [];

    public event PropertyChangedEventHandler PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
