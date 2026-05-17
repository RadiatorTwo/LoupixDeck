namespace LoupixDeck.Services.SystemPower;

/// <summary>Fallback for platforms without a power-event hook.</summary>
public sealed class NoOpSystemPowerService : ISystemPowerService
{
    public event EventHandler Suspending { add { } remove { } }
    public event EventHandler Resuming { add { } remove { } }
    public void StartMonitoring() { }
}
