namespace LoupixDeck.Services.SystemPower;

/// <summary>Fallback for platforms without a lock-state source.</summary>
public sealed class NoOpSessionLockService : SessionLockServiceBase
{
    protected override void Start() { }
}
