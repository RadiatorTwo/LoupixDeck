using Avalonia.Threading;
using LoupixDeck.Models;
using LoupixDeck.Models.Companion;
using Microsoft.Extensions.DependencyInjection;

namespace LoupixDeck.Services.Companion;

/// <summary>
/// Gives a companion group one shared context: every companion mirrors its master's profiles and
/// workspaces (see <see cref="CompanionStructureSync"/>) and switches to whatever profile and
/// workspace the master shows. Structure changes reach companions that are unplugged too, by
/// writing their config files; a companion that comes up takes the master's current state.
/// Root-level singleton; all work runs on the UI thread.
/// </summary>
public interface ICompanionContextSync
{
    /// <summary>Raised on the UI thread after a device's mirrored structure was rewritten (it joined,
    /// left, or its master's profiles or workspaces changed). Carries the device key.</summary>
    event Action<string> LinkedStructureChanged;
}

public sealed class CompanionContextSyncService : ICompanionContextSync
{
    private static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(300);

    private readonly ICompanionCoordinator _coordinator;
    private readonly IDeviceHostRegistry _registry;
    private readonly IConfigService _configService;
    private readonly ICompanionNavigation _navigation;

    // Each running host's event handlers, so a removed host is unhooked exactly once.
    private readonly Dictionary<DeviceHost, HostHooks> _hostHooks = new();

    // Masters whose config was saved since the last debounced sync.
    private readonly HashSet<string> _savedMasters = new(StringComparer.OrdinalIgnoreCase);

    // Structure fingerprint each master's companions were last synced to, so a save that changed
    // only buttons or pages does not reload every offline companion's config file.
    private readonly Dictionary<string, string> _syncedFingerprints = new(StringComparer.OrdinalIgnoreCase);

    private DispatcherTimer _saveTimer;

    public event Action<string> LinkedStructureChanged;

    public CompanionContextSyncService(ICompanionCoordinator coordinator, IDeviceHostRegistry registry,
        IConfigService configService, ICompanionNavigation navigation)
    {
        _coordinator = coordinator;
        _registry = registry;
        _configService = configService;
        _navigation = navigation;

        foreach (DeviceHost host in _registry.Hosts)
            OnHostAdded(host);

        _registry.HostAdded += OnHostAdded;
        _registry.HostRemoved += OnHostRemoved;
        _coordinator.GroupsChanged += OnGroupsChanged;
        _coordinator.DeviceReady += OnDeviceReady;
        _configService.ConfigSaved += OnConfigSaved;
    }

    // ── Triggers ────────────────────────────────────────────────────────────

    private void OnGroupsChanged() => OnUiThread(() =>
    {
        SyncAllDevices();

        // Page follow may just have been turned on: bring the companions to their master's pages.
        foreach (DeviceHost host in _registry.Hosts)
        {
            string key = host.Device.ScopeKey;
            if (_coordinator.IsReady(host) && _coordinator.GetPageFollow(key) != CompanionPageFollowMode.Off)
                FollowCompanionsOf(host);
        }
    });

    private void OnHostAdded(DeviceHost host)
    {
        HostHooks hooks = new(
            host.Provider.GetRequiredService<IWorkspaceActivationService>(),
            _ => OnActiveWorkspaceChanged(host),
            host.Controller.PageManager,
            (_, index) => OnMasterPageChanged(host, CompanionPageKind.Touch, index),
            (side, _, index) => OnMasterPageChanged(host, KindOf(side), index));
        lock (_hostHooks)
        {
            if (!_hostHooks.TryAdd(host, hooks)) return;
        }
        hooks.Activation.ActiveWorkspaceChanged += hooks.WorkspaceChanged;
        hooks.Pages.OnTouchPageChanged += hooks.TouchPageChanged;
        hooks.Pages.OnRotaryPageChanged += hooks.RotaryPageChanged;
    }

    private void OnHostRemoved(DeviceHost host)
    {
        HostHooks hooks;
        lock (_hostHooks)
        {
            if (!_hostHooks.Remove(host, out hooks)) return;
        }
        hooks.Activation.ActiveWorkspaceChanged -= hooks.WorkspaceChanged;
        hooks.Pages.OnTouchPageChanged -= hooks.TouchPageChanged;
        hooks.Pages.OnRotaryPageChanged -= hooks.RotaryPageChanged;
    }

    private sealed record HostHooks(
        IWorkspaceActivationService Activation,
        Action<Workspace> WorkspaceChanged,
        IPageManager Pages,
        Action<int, int> TouchPageChanged,
        Action<RotarySide, int, int> RotaryPageChanged);

    /// <summary>A master switched profile or workspace (a profile switch raises this too): its
    /// running companions follow. On a companion this is the echo of its own follow and is ignored.</summary>
    private void OnActiveWorkspaceChanged(DeviceHost host) => OnUiThread(() =>
    {
        if (_coordinator.IsMaster(host.Device.ScopeKey))
            FollowCompanionsOf(host);
    });

    /// <summary>
    /// A master showed another page: with page follow on, its running companions that already show
    /// the same workspace show the same page number. During a workspace switch they are still
    /// following and are aligned once they arrived (see <see cref="FollowAsync"/>).
    /// </summary>
    private void OnMasterPageChanged(DeviceHost host, CompanionPageKind kind, int index) => OnUiThread(() =>
    {
        string masterKey = host.Device.ScopeKey;
        CompanionPageFollowMode mode = _coordinator.GetPageFollow(masterKey);
        if (mode == CompanionPageFollowMode.Off ||
            (kind != CompanionPageKind.Touch && mode != CompanionPageFollowMode.TouchAndRotaryPages))
            return;

        Guid workspaceId = host.Controller.Config.ActiveWorkspaceId;
        foreach (string companionKey in _coordinator.GetCompanionKeys(masterKey))
            _ = _navigation.FollowPageIndex(masterKey, companionKey, workspaceId, kind, index);
    });

    private static CompanionPageKind KindOf(RotarySide side) => side switch
    {
        RotarySide.Left => CompanionPageKind.RotaryLeft,
        RotarySide.Right => CompanionPageKind.RotaryRight,
        _ => CompanionPageKind.Rotary
    };

    /// <summary>A device finished starting or reconnected: a companion takes its master's state, a
    /// master hands its state to its companions.</summary>
    private void OnDeviceReady(DeviceHost host)
    {
        string key = host.Device.ScopeKey;
        if (_coordinator.IsMaster(key))
        {
            FollowCompanionsOf(host);
            return;
        }

        string masterKey = _coordinator.GetMasterKey(key);
        if (masterKey == null && host.Provider.GetRequiredService<LoupedeckConfig>().CompanionLink == null)
            return;

        SyncDevice(key, masterKey);
        if (masterKey != null)
            _ = FollowAsync(host, masterKey);
    }

    private void OnConfigSaved(string filePath) => OnUiThread(() =>
    {
        string master = _coordinator.GetKnownDevices()
            .Where(d => _coordinator.IsMaster(d.Key))
            .Select(d => d.Key)
            .FirstOrDefault(key => PathEquals(_coordinator.GetConfigPath(key), filePath));
        if (master == null) return;

        _savedMasters.Add(master);
        _saveTimer ??= CreateSaveTimer();
        _saveTimer.Stop();
        _saveTimer.Start();
    });

    private DispatcherTimer CreateSaveTimer()
    {
        DispatcherTimer timer = new() { Interval = SaveDebounce };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            List<string> masters = [.. _savedMasters];
            _savedMasters.Clear();
            foreach (string master in masters)
                SyncCompanionsOf(master, force: false);
        };
        return timer;
    }

    // ── Structure ───────────────────────────────────────────────────────────

    /// <summary>Brings every known device in line with its current role: companions are linked or
    /// reconciled, devices that left a group get their own profiles back.</summary>
    private void SyncAllDevices()
    {
        _syncedFingerprints.Clear();
        foreach (CompanionDeviceInfo device in _coordinator.GetKnownDevices())
            SyncDevice(device.Key, _coordinator.GetMasterKey(device.Key));
    }

    // ── Following ───────────────────────────────────────────────────────────

    private void FollowCompanionsOf(DeviceHost masterHost)
    {
        string masterKey = masterHost.Device.ScopeKey;
        LoupedeckConfig master = masterHost.Provider.GetRequiredService<LoupedeckConfig>();

        foreach (string companionKey in _coordinator.GetCompanionKeys(masterKey))
        {
            DeviceHost companionHost = _coordinator.ResolveHost(companionKey);
            if (!_coordinator.IsReady(companionHost)) continue;

            // A profile the master just created may not be saved yet: mirror it before switching.
            SyncDevice(companionKey, masterKey, master);
            _ = FollowAsync(companionHost, masterKey);
        }
    }

    /// <summary>Moves a running companion to its master's active profile and workspace. While the
    /// master is not running the companion stays where it is, as long as that still exists.
    /// With page follow on it then shows the master's page numbers, and a page target queued for the
    /// workspace it arrives in opens afterwards, so an explicit target wins.</summary>
    private async Task FollowAsync(DeviceHost companionHost, string masterKey)
    {
        // Runs synchronously up to the first await, so a target queued right after the master's
        // switch already sees this follow in progress.
        _navigation.BeginFollow(companionHost.Device.ScopeKey);
        try
        {
            IWorkspaceActivationService activation = companionHost.Provider.GetRequiredService<IWorkspaceActivationService>();
            DeviceHost masterHost = _coordinator.ResolveHost(masterKey);

            if (masterHost != null)
            {
                LoupedeckConfig master = masterHost.Provider.GetRequiredService<LoupedeckConfig>();
                await activation.FollowMaster(master.ActiveProfileId, master.ActiveWorkspaceId);
                await _navigation.AlignPagesWithMaster(masterHost, companionHost, _coordinator.GetPageFollow(masterKey));
            }
            else
            {
                LoupedeckConfig config = companionHost.Provider.GetRequiredService<LoupedeckConfig>();
                await activation.FollowMaster(config.ActiveProfileId, config.ActiveWorkspaceId);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Companions] '{companionHost.Device.ScopeKey}' could not follow '{masterKey}': {ex.Message}");
        }
        finally
        {
            await _navigation.EndFollow(companionHost);
        }
    }

    /// <summary>
    /// After a running device's profiles were swapped or rewritten, shows a profile that exists: a
    /// companion follows its master, a device that left its group reopens its own active profile.
    /// </summary>
    private void RefreshActiveState(DeviceHost host, string masterKey)
    {
        if (!_coordinator.IsReady(host)) return;

        if (masterKey != null)
        {
            _ = FollowAsync(host, masterKey);
            return;
        }

        LoupedeckConfig config = host.Provider.GetRequiredService<LoupedeckConfig>();
        if (config.ActiveProfile is { } profile)
            _ = host.Provider.GetRequiredService<IWorkspaceActivationService>().ActivateProfile(profile.Id, manual: false);
    }

    private void SyncCompanionsOf(string masterKey, bool force)
    {
        LoupedeckConfig master = _coordinator.GetDeviceConfig(masterKey);
        if (master == null) return;

        string fingerprint = CompanionStructureSync.StructureFingerprint(master);
        if (!force && _syncedFingerprints.TryGetValue(masterKey, out string synced) &&
            string.Equals(synced, fingerprint, StringComparison.Ordinal))
            return;

        foreach (string companionKey in _coordinator.GetCompanionKeys(masterKey))
            SyncDevice(companionKey, masterKey, master);

        _syncedFingerprints[masterKey] = fingerprint;
    }

    /// <summary>Links, relinks, unlinks or reconciles one device and saves it when anything changed.
    /// A running device is changed in memory and saved by its controller; an unplugged one is read
    /// from and written back to its config file.</summary>
    private void SyncDevice(string deviceKey, string masterKey, LoupedeckConfig master = null)
    {
        try
        {
            DeviceHost host = _coordinator.ResolveHost(deviceKey);
            LoupedeckConfig config = _coordinator.GetDeviceConfig(deviceKey);
            if (config == null) return;

            // Nothing to do for a device that neither follows a master nor still holds a link.
            if (masterKey == null && config.CompanionLink == null) return;

            master ??= masterKey == null ? null : _coordinator.GetDeviceConfig(masterKey);
            if (!CompanionStructureSync.ApplyAndAttachGeometry(config, masterKey, master))
                return;

            if (host != null)
            {
                host.Controller.SaveConfig();
                RefreshActiveState(host, masterKey);
            }
            else if (_coordinator.GetConfigPath(deviceKey) is { } path)
            {
                _configService.SaveConfig(config, path);
            }

            Console.WriteLine($"[Companions] Synced the profiles of '{deviceKey}' ({(masterKey == null ? "standalone" : $"following '{masterKey}'")}).");
            LinkedStructureChanged?.Invoke(deviceKey);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Companions] Syncing the profiles of '{deviceKey}' failed: {ex.Message}");
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static void OnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    private static bool PathEquals(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
