using LoupixDeck.Models;
using LoupixDeck.Models.Companion;

namespace LoupixDeck.Services.Companion;

/// <summary>One device as the companion editor sees it: its key, a human label, whether it is
/// reachable right now and the role its group assigns it.</summary>
public sealed record CompanionDeviceInfo(string Key, string DisplayName, bool IsOnline, CompanionRole Role);

/// <summary>
/// The master/companion logic on top of the running device set. Owns the companion groups
/// (loaded from and saved to <c>companions.json</c>), derives each device's role from them and
/// tracks which devices are reachable.
///
/// Devices are identified by scope key (slug + serial) throughout. Actual routing to a running
/// device goes through <see cref="IDeviceHostRegistry"/> via <see cref="ResolveHost"/>.
/// Root-level singleton, shared by every device.
/// </summary>
public interface ICompanionCoordinator
{
    /// <summary>The configured groups. Edit them only through the methods below so validation and
    /// persistence stay in one place.</summary>
    IReadOnlyList<CompanionGroup> Groups { get; }

    /// <summary>Raised after the group set changed (any editing method). Caller's thread.</summary>
    event Action GroupsChanged;

    /// <summary>Raised when a device came online or went offline (host added/removed, link up).
    /// Carries the device key. May fire on a background thread.</summary>
    event Action<string> DeviceOnlineStateChanged;

    /// <summary>Raised on the UI thread when a device's controller finished initializing, and again
    /// each time its serial link comes back up afterwards (hot-plug or reconnect).</summary>
    event Action<DeviceHost> DeviceReady;

    /// <summary>
    /// Tells the coordinator that the host's controller finished <c>Initialize</c>. Before that the
    /// controller still applies its startup page, which would overwrite any state applied on connect,
    /// so <see cref="DeviceReady"/> is only raised for initialized hosts.
    /// </summary>
    void DeviceInitialized(DeviceHost host);

    /// <summary>True once the host's controller finished initializing (see <see cref="DeviceInitialized"/>).</summary>
    bool IsReady(DeviceHost host);

    // ── Roles ───────────────────────────────────────────────────────────────

    CompanionRole GetRole(string deviceKey);
    bool IsMaster(string deviceKey);
    bool IsCompanion(string deviceKey);

    /// <summary>The group the device belongs to (as master or companion), or null.</summary>
    CompanionGroup FindGroup(string deviceKey);

    /// <summary>True when <paramref name="companionKey"/> is a companion in the group whose master
    /// is <paramref name="masterKey"/>.</summary>
    bool IsCompanionOf(string masterKey, string companionKey);

    /// <summary>The companions of the given master, in group order. Empty for non-masters.</summary>
    IReadOnlyList<string> GetCompanionKeys(string masterKey);

    /// <summary>The master a companion follows, or null when the device is not an active companion.</summary>
    string GetMasterKey(string companionKey);

    // ── Devices ─────────────────────────────────────────────────────────────

    /// <summary>The running host for a device key, or null when it is not brought up.</summary>
    DeviceHost ResolveHost(string deviceKey);

    /// <summary>
    /// The device's configuration: the live instance when the device is running, otherwise a copy
    /// read from its config file so an offline companion's profiles, workspaces and pages can still
    /// be browsed. Treat an offline copy as read-only. Null when the device has no config.
    /// </summary>
    LoupedeckConfig GetDeviceConfig(string deviceKey);

    /// <summary>The path of the device's config file (whether or not it exists yet), or null when the
    /// device is neither running nor has a config file.</summary>
    string GetConfigPath(string deviceKey);

    /// <summary>True when the device is brought up and its serial link is open.</summary>
    bool IsOnline(string deviceKey);

    /// <summary>Human label for a device key ("Razer Stream Controller · 1A2B3C4D"). Falls back
    /// to the key when nothing else is known about it.</summary>
    string GetDisplayName(string deviceKey);

    /// <summary>Every device the editor can offer: running devices plus devices that have a config
    /// file but are unplugged, plus keys referenced by a group that match neither (so a removed
    /// companion still shows up and can be taken out of its group).</summary>
    IReadOnlyList<CompanionDeviceInfo> GetKnownDevices();

    /// <summary>True when the device can be told apart from every other known device: it reports a
    /// serial, or it is the only known device of its model. Only such devices may join a group.</summary>
    bool CanJoinGroup(string deviceKey);

    // ── Editing (all persist immediately) ───────────────────────────────────

    CompanionGroup CreateGroup(string name);
    void RemoveGroup(Guid groupId);
    void RenameGroup(Guid groupId, string name);

    /// <summary>Sets whether the group's companions page along with its master.</summary>
    void SetPageFollow(Guid groupId, CompanionPageFollowMode mode);

    /// <summary>The page follow mode of the active group the master leads; <see cref="CompanionPageFollowMode.Off"/>
    /// for a device that is not an active master or an unknown stored value.</summary>
    CompanionPageFollowMode GetPageFollow(string masterKey);

    /// <summary>Makes the device the group's master. Refused (false + reason) when the device
    /// already belongs to another group or is a companion of this one.</summary>
    bool TrySetMaster(Guid groupId, string deviceKey, out string error);

    /// <summary>Adds the device as a companion. Refused when it already belongs to any group
    /// (including as this group's master) — groups never nest.</summary>
    bool TryAddCompanion(Guid groupId, string deviceKey, out string error);

    void RemoveCompanion(Guid groupId, string deviceKey);
}
