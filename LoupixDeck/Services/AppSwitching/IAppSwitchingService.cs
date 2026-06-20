namespace LoupixDeck.Services.AppSwitching;

/// <summary>
/// Drives automatic page switching from the OS foreground window. Subscribes to
/// <c>IActiveWindowMonitor</c>, matches the active app against the user's rules
/// (<c>LoupedeckConfig.AppPageBindings</c>) and flips the deck to the bound page.
/// </summary>
public interface IAppSwitchingService
{
    /// <summary>Subscribes to the monitor and starts it. Call once on the UI thread.</summary>
    void Start();
}
