namespace LoupixDeck.Services.AppActivation;

/// <summary>Platforms without an implementation: always starts the application.</summary>
public sealed class NoOpAppWindowActivator : IAppWindowActivator
{
    public Task<AppActivationOutcome> ActivateAsync(string processName, CancellationToken cancellationToken)
        => Task.FromResult(AppActivationOutcome.NotRunning);
}
