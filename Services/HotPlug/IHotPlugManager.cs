using LoupixDeck.Registry;

namespace LoupixDeck.Services.HotPlug;

/// <summary>
/// Watches for USB device changes at runtime and reconciles them against the
/// running <see cref="IDeviceHostRegistry"/> (issue #116 phase 3b). It owns the
/// diff but not the bring-up: building a device's child provider + view model is
/// App-level work, so it raises events that App handles on the UI thread.
///
/// Both events fire on a background (debounce-timer) thread — handlers must
/// marshal to the UI thread before touching view models / the device.
/// </summary>
public interface IHotPlugManager
{
    /// <summary>A supported device appeared that has no running host yet.</summary>
    event Action<ResolvedDevice> DeviceAttached;

    /// <summary>A running host's device is no longer connected.</summary>
    event Action<DeviceHost> DeviceDetached;

    /// <summary>Start the underlying watcher and arm reconciliation.</summary>
    void Start();
}
