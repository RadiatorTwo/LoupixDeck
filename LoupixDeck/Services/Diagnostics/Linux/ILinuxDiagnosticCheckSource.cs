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
    /// Called once at the start of every run, on a worker thread and before
    /// <see cref="CreateChecks"/>, so a source whose checks depend on something that has to be
    /// probed (and that the user may have changed while the app was open) can look again first.
    /// Must not throw. Not called for the check counts the page shows, which only read what the
    /// source already knows. Sources that need nothing keep the default.
    /// </summary>
    Task PrepareAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// The checks for the current state of the system. Must not throw: a source that cannot
    /// enumerate returns its own explaining check, or nothing at all.
    /// </summary>
    IReadOnlyList<ILinuxDiagnosticCheck> CreateChecks();
}
