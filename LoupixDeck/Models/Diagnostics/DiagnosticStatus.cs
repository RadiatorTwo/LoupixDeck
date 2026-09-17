namespace LoupixDeck.Models.Diagnostics;

/// <summary>
/// Outcome of a single diagnostic check. <see cref="Unknown"/> is the default value on
/// purpose: a result that was never filled in must never read as <see cref="Pass"/>.
/// </summary>
public enum DiagnosticStatus
{
    /// <summary>The check itself could not run reliably. Never present this as a pass.</summary>
    Unknown = 0,

    /// <summary>The check does not apply to this system.</summary>
    Skipped,

    /// <summary>The requirement is satisfied.</summary>
    Pass,

    /// <summary>The functionality is possible but restricted or unusual.</summary>
    Warning,

    /// <summary>A concrete feature cannot work.</summary>
    Fail
}
