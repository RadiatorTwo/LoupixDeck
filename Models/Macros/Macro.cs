using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LoupixDeck.Models.Macros;

/// <summary>How re-triggering a macro that is already running behaves.</summary>
public enum MacroExecutionMode
{
    /// <summary>Ignore the new trigger while a run is in progress (default).</summary>
    RunOnce,

    /// <summary>Cancel the current run and start a fresh one.</summary>
    RestartOnTrigger,

    /// <summary>Allow multiple concurrent runs of the same macro.</summary>
    AllowParallel
}

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

    private MacroExecutionMode _executionMode = MacroExecutionMode.RunOnce;

    /// <summary>
    /// Behaviour when the macro is triggered again while already running. Defaults to
    /// RunOnce (enum 0), so files written before this field default correctly on load.
    /// </summary>
    public MacroExecutionMode ExecutionMode
    {
        get => _executionMode;
        set
        {
            if (_executionMode == value) return;
            _executionMode = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<MacroStep> Steps { get; set; } = [];

    /// <summary>All execution modes — bound by the editor's ComboBox.</summary>
    public static MacroExecutionMode[] AllExecutionModes { get; } = Enum.GetValues<MacroExecutionMode>();

    public event PropertyChangedEventHandler PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
