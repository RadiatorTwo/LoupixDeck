namespace LoupixDeck.Services.SystemPower;

/// <summary>
/// Notifies subscribers when the host enters or leaves a suspended state, so
/// the device can be cleared/restored around system sleep. Implementations
/// are platform-specific; on platforms without a working hook this is a
/// no-op (no events fire, StartMonitoring returns quietly).
/// </summary>
public interface ISystemPowerService
{
    event EventHandler Suspending;
    event EventHandler Resuming;
    void StartMonitoring();
}
