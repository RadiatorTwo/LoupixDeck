namespace LoupixDeck.Services.AppActivation;

/// <summary>Platforms without an implementation: always starts the application.</summary>
public sealed class NoOpAppWindowActivator : IAppWindowActivator
{
    public Task<AppActivationOutcome> ActivateAsync(string processName, IReadOnlyCollection<string> windowClasses,
        CancellationToken cancellationToken)
        => Task.FromResult(AppActivationOutcome.NotRunning);
}
