using LoupixDeck.Localization;

namespace LoupixDeck.Services.Companion;

/// <summary>
/// The texts that explain why a companion's profile and workspace controls are locked, naming its
/// master and whether that master is connected. Shared by the main window header, the settings panes
/// and the command editors so every place says the same thing.
/// </summary>
public static class CompanionStatusText
{
    /// <summary>The master's display name, or null when <paramref name="deviceKey"/> is not an active companion.</summary>
    public static string MasterName(ICompanionCoordinator coordinator, string deviceKey) =>
        coordinator.GetMasterKey(deviceKey) is { } masterKey ? coordinator.GetDisplayName(masterKey) : null;

    /// <summary>True when the device is an active companion whose master is not connected.</summary>
    public static bool IsMasterOffline(ICompanionCoordinator coordinator, string deviceKey) =>
        coordinator.GetMasterKey(deviceKey) is { } masterKey && !coordinator.IsOnline(masterKey);

    /// <summary>Short label, e.g. "Follows Loupedeck Live S"; null when the device is not an active companion.</summary>
    public static string FollowsMaster(ICompanionCoordinator coordinator, string deviceKey) =>
        MasterName(coordinator, deviceKey) is { } name
            ? Loc.Tr(IsMasterOffline(coordinator, deviceKey) ? "Companion_FollowsOfflineMasterFmt" : "Companion_FollowsMasterFmt", name)
            : null;

    /// <summary>One or two sentences on what the master controls and what stays editable; null when
    /// the device is not an active companion.</summary>
    public static string LockExplanation(ICompanionCoordinator coordinator, string deviceKey) =>
        MasterName(coordinator, deviceKey) is { } name
            ? Loc.Tr(IsMasterOffline(coordinator, deviceKey) ? "Companion_LockExplanationOfflineFmt" : "Companion_LockExplanationFmt", name)
            : null;
}
