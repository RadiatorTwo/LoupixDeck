using LoupixDeck.Models;
using LoupixDeck.Services;

namespace LoupixDeck.Controllers;

/// <summary>
/// Lifecycle contract every device controller exposes to the rest of the app.
/// Keeps MainWindowViewModel and PageCommands decoupled from the concrete
/// controller implementation now that we support more than one device family.
/// </summary>
public interface IDeviceController
{
    IPageManager PageManager { get; }
    LoupedeckConfig Config { get; }

    /// <summary>
    /// True while the device is in the manually-off / suspended state — inputs
    /// are suppressed unless the source button has EnableWhenOff set.
    /// </summary>
    bool IsDeviceOff { get; }

    Task Initialize(string port = null, int baudrate = 0);
    void SaveConfig();

    /// <summary>
    /// Tears the controller down for a hot-unplug (issue #116 phase 3b): closes the
    /// serial connection (stopping the device's auto-reconnect loop), detaches any
    /// plugin side-strip providers, and unsubscribes from page/config/device events
    /// so nothing keeps drawing to the gone device. The owning child provider is
    /// intentionally NOT disposed (it would dispose the shared root singletons it
    /// forwards), so this method is the full device-local cleanup.
    /// </summary>
    void Shutdown();

    /// <summary>Sets brightness to 0 and turns off all LED buttons. Input from
    /// the device is then ignored unless a button opts in via EnableWhenOff.</summary>
    Task ClearDeviceState();

    /// <summary>Restores brightness, LED colours and the current touch page.</summary>
    Task RestoreDeviceState();

    /// <summary>
    /// Repaints the current touch page from config. No-op while the device is off
    /// or folder/exclusive mode owns the screen. Used after a plugin hot-reload so
    /// added/removed command buttons reflect on the device. UI thread.
    /// </summary>
    Task RedrawCurrentTouchPage();

    /// <summary>Convenience: flips between Clear and Restore.</summary>
    Task ToggleDeviceState();

    /// <summary>
    /// Detaches any plugin-override side-strip providers currently driving a strip
    /// (calls their OnDetach). Used before a plugin unload so a live provider can't
    /// pin its collectible load context. No-op on devices without side strips.
    /// </summary>
    void DetachAllSideStripProviders();

    /// <summary>
    /// Re-evaluates plugin-override attachment for both side strips and repaints them
    /// (a reloaded provider re-attaches; an orphaned binding falls back to segmented).
    /// No-op while the device is off / folder / exclusive mode owns it, or on devices
    /// without side strips.
    /// </summary>
    Task RefreshSideStrips();
}
