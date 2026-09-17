namespace LoupixDeck.Services.Diagnostics.Linux;

/// <summary>
/// Supplies checks that only exist once the system has been looked at - one set per connected
/// deck (issue #258 phase 2). A statically registered <see cref="ILinuxDiagnosticCheck"/> cannot
/// express that: how many decks are plugged in is known at run time, and it changes while the
/// app is open.
///
/// The orchestrator asks the source for its checks at the start of every run, so a deck that was
/// plugged in after the last run is diagnosed without restarting anything.
/// </summary>
public interface ILinuxDiagnosticCheckSource
{
    /// <summary>
    /// The checks for the current state of the system. Must not throw: a source that cannot
    /// enumerate returns its own explaining check, or nothing at all.
    /// </summary>
    IReadOnlyList<ILinuxDiagnosticCheck> CreateChecks();
}
