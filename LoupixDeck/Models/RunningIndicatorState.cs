namespace LoupixDeck.Models;

/// <summary>The dot an application key shows for the program it launches (issue #568).</summary>
public enum RunningIndicatorState
{
    /// <summary>No dot: the option is off, or the program does not run.</summary>
    None,

    /// <summary>The program runs.</summary>
    Running,

    /// <summary>The program runs and one of its windows has focus.</summary>
    Active
}
