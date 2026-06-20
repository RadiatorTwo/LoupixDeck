using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;

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
[ObservableObject]
public partial class Macro
{
    /// <summary>
    /// Unique macro name. Must not contain '(' ')' ',' or '&amp;' — those would break
    /// the command parser when the macro is invoked as System.Macro(Name).
    /// </summary>
    [ObservableProperty]
    public partial string Name { get; set; }

    /// <summary>
    /// Behaviour when the macro is triggered again while already running. Defaults to
    /// RunOnce (enum 0), so files written before this field default correctly on load.
    /// </summary>

    [ObservableProperty]
    public partial MacroExecutionMode ExecutionMode { get; set; } = MacroExecutionMode.RunOnce;

    public ObservableCollection<MacroStep> Steps { get; set; } = [];

    /// <summary>All execution modes — bound by the editor's ComboBox.</summary>
    public static ImmutableArray<MacroExecutionMode> AllExecutionModes { get; } = ImmutableCollectionsMarshal.AsImmutableArray(Enum.GetValues<MacroExecutionMode>());
}
