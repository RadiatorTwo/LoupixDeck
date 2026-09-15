using LoupixDeck.Localization;
using LoupixDeck.Models;
using LoupixDeck.Models.Companion;
using LoupixDeck.Registry;
using LoupixDeck.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace LoupixDeck.Services.Companion;

/// <inheritdoc cref="ICompanionCoordinator"/>
public sealed class CompanionCoordinator : ICompanionCoordinator
{
    private readonly ICompanionStore _store;
    private readonly IDeviceHostRegistry _registry;
    private readonly IConfigService _configService;
    private readonly Lock _gate = new();
    private readonly CompanionConfig _config;

    // Hosts whose DeviceConnected we subscribed to, so a removed host is unhooked exactly once.
    private readonly Dictionary<DeviceHost, EventHandler> _connectHandlers = new();

    // Hosts whose controller finished Initialize. Only these raise DeviceReady.
    private readonly HashSet<DeviceHost> _initializedHosts = [];

    // Groups paused at runtime. Deliberately not saved: a forgotten pause must not outlive the session.
    private readonly HashSet<Guid> _pausedGroups = [];

    public event Action GroupsChanged;
    public event Action<string> DeviceOnlineStateChanged;
    public event Action<DeviceHost> DeviceReady;

    public CompanionCoordinator(ICompanionStore store, IDeviceHostRegistry registry, IConfigService configService)
    {
        _store = store;
        _registry = registry;
        _configService = configService;
        _config = _store.Load();

        if (UpgradeSlugOnlyKeys(ActiveDeviceResolver.EnumerateConfigDevices()))
            SaveQuietly();

        // Hosts registered before this instance existed (none in practice — the root is built
        // before pass 1 — but a late-constructed coordinator must still see them).
        foreach (DeviceHost host in _registry.Hosts)
            OnHostAdded(host);

        _registry.HostAdded += OnHostAdded;
        _registry.HostRemoved += OnHostRemoved;
    }

    public IReadOnlyList<CompanionGroup> Groups
    {
        get { lock (_gate) return _config.Groups.ToArray(); }
    }

    // ── Roles ───────────────────────────────────────────────────────────────

    /// <summary>A group only assigns roles once it is complete: a master with no companion controls
    /// nothing, and companions without a master would be locked out of their own navigation.</summary>
    private static bool IsEffective(CompanionGroup group) =>
        !string.IsNullOrWhiteSpace(group.MasterDeviceKey) && group.CompanionDeviceKeys.Count > 0;

    public CompanionRole GetRole(string deviceKey)
    {
        if (string.IsNullOrWhiteSpace(deviceKey)) return CompanionRole.None;
        lock (_gate)
        {
            foreach (CompanionGroup group in _config.Groups)
            {
                if (!IsEffective(group)) continue;
                if (KeyEquals(group.MasterDeviceKey, deviceKey)) return CompanionRole.Master;
                if (group.CompanionDeviceKeys.Contains(deviceKey, StringComparer.OrdinalIgnoreCase))
                    return CompanionRole.Companion;
            }
        }
        return CompanionRole.None;
    }

    public bool IsMaster(string deviceKey) => GetRole(deviceKey) == CompanionRole.Master;
    public bool IsCompanion(string deviceKey) => GetRole(deviceKey) == CompanionRole.Companion;

    public bool IsPaused(string deviceKey)
    {
        if (GetRole(deviceKey) == CompanionRole.None) return false;
        CompanionGroup group = FindGroup(deviceKey);
        lock (_gate) return group != null && _pausedGroups.Contains(group.Id);
    }

    public bool IsFollowingMaster(string deviceKey) => IsCompanion(deviceKey) && !IsPaused(deviceKey);

    public void SetPaused(string masterKey, bool paused)
    {
        if (!IsMaster(masterKey) || FindGroup(masterKey) is not { } group) return;

        bool changed;
        lock (_gate) changed = paused ? _pausedGroups.Add(group.Id) : _pausedGroups.Remove(group.Id);
        if (!changed) return;

        Console.WriteLine($"[Companions] Group '{group.Name}' {(paused ? "paused" : "resumed")}.");
        GroupsChanged?.Invoke();
    }

    public CompanionGroup FindGroup(string deviceKey)
    {
        if (string.IsNullOrWhiteSpace(deviceKey)) return null;
        lock (_gate)
        {
            return _config.Groups.FirstOrDefault(g =>
                KeyEquals(g.MasterDeviceKey, deviceKey) ||
                g.CompanionDeviceKeys.Contains(deviceKey, StringComparer.OrdinalIgnoreCase));
        }
    }

    public bool IsCompanionOf(string masterKey, string companionKey)
    {
        if (string.IsNullOrWhiteSpace(masterKey) || string.IsNullOrWhiteSpace(companionKey)) return false;
        lock (_gate)
        {
            return _config.Groups.Any(g =>
                IsEffective(g) &&
                KeyEquals(g.MasterDeviceKey, masterKey) &&
                g.CompanionDeviceKeys.Contains(companionKey, StringComparer.OrdinalIgnoreCase));
        }
    }

    public IReadOnlyList<string> GetCompanionKeys(string masterKey)
    {
        if (string.IsNullOrWhiteSpace(masterKey)) return [];
        lock (_gate)
        {
            CompanionGroup group = _config.Groups.FirstOrDefault(g => IsEffective(g) && KeyEquals(g.MasterDeviceKey, masterKey));
            return group?.CompanionDeviceKeys.ToArray() ?? [];
        }
    }

    public string GetMasterKey(string companionKey)
    {
        if (string.IsNullOrWhiteSpace(companionKey)) return null;
        lock (_gate)
        {
            return _config.Groups.FirstOrDefault(g =>
                IsEffective(g) && g.CompanionDeviceKeys.Contains(companionKey, StringComparer.OrdinalIgnoreCase))
                ?.MasterDeviceKey;
        }
    }

    // ── Devices ─────────────────────────────────────────────────────────────

    public DeviceHost ResolveHost(string deviceKey)
    {
        if (string.IsNullOrWhiteSpace(deviceKey)) return null;
        // Exact scope-key match only — the registry's Find also accepts bare serials and slugs,
        // which could alias two identical units onto one host.
        return _registry.Hosts.FirstOrDefault(h => KeyEquals(h.Device.ScopeKey, deviceKey));
    }

    public LoupedeckConfig GetDeviceConfig(string deviceKey)
    {
        DeviceHost host = ResolveHost(deviceKey);
        if (host != null)
            return host.Provider.GetRequiredService<LoupedeckConfig>();

        string path = GetConfigPath(deviceKey);
        if (path == null)
            return null;

        try
        {
            return _configService.LoadConfig<LoupedeckConfig>(path);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Companions] Could not read the config of '{deviceKey}': {ex.Message}");
            return null;
        }
    }

    public string GetConfigPath(string deviceKey)
    {
        if (string.IsNullOrWhiteSpace(deviceKey)) return null;

        ResolvedDevice device = ResolveHost(deviceKey)?.Device
            ?? ActiveDeviceResolver.EnumerateConfigDevices().FirstOrDefault(d => KeyEquals(d.ScopeKey, deviceKey));
        return device == null ? null : FileDialogHelper.GetConfigPath(device.Info, device.Serial);
    }

    public bool IsOnline(string deviceKey) => ResolveHost(deviceKey)?.Controller.IsDeviceConnected == true;

    public string GetDisplayName(string deviceKey)
    {
        if (string.IsNullOrWhiteSpace(deviceKey)) return "(no device)";

        DeviceHost host = ResolveHost(deviceKey);
        if (host != null) return Label(host.Device);

        ResolvedDevice configured = ActiveDeviceResolver.EnumerateConfigDevices()
            .FirstOrDefault(d => KeyEquals(d.ScopeKey, deviceKey));
        return configured != null ? Label(configured) : deviceKey;
    }

    public IReadOnlyList<CompanionDeviceInfo> GetKnownDevices()
    {
        Dictionary<string, CompanionDeviceInfo> result = new(StringComparer.OrdinalIgnoreCase);

        foreach (DeviceHost host in _registry.Hosts)
        {
            string key = host.Device.ScopeKey;
            result[key] = new CompanionDeviceInfo(key, Label(host.Device), host.Controller.IsDeviceConnected, GetRole(key));
        }

        foreach (ResolvedDevice device in ActiveDeviceResolver.EnumerateConfigDevices())
        {
            string key = device.ScopeKey;
            if (!result.ContainsKey(key))
                result[key] = new CompanionDeviceInfo(key, Label(device), false, GetRole(key));
        }

        lock (_gate)
        {
            foreach (CompanionGroup group in _config.Groups)
            foreach (string key in CompanionGroupValidator.Members(group))
                if (!result.ContainsKey(key))
                    result[key] = new CompanionDeviceInfo(key, key, false, GetRole(key));
        }

        return result.Values.OrderBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string Label(ResolvedDevice device)
    {
        if (string.IsNullOrEmpty(device.Serial)) return device.Info.Name;
        string serial = device.Serial.Length <= 8 ? device.Serial : device.Serial[^8..];
        return $"{device.Info.Name} · {serial}";
    }

    // ── Editing ─────────────────────────────────────────────────────────────

    public CompanionGroup CreateGroup(string name)
    {
        CompanionGroup group = new() { Name = name ?? string.Empty };
        lock (_gate) _config.Groups.Add(group);
        Persist();
        return group;
    }

    public void RemoveGroup(Guid groupId)
    {
        bool removed;
        lock (_gate) removed = _config.Groups.RemoveAll(g => g.Id == groupId) > 0;
        if (removed) Persist();
    }

    public void RenameGroup(Guid groupId, string name)
    {
        lock (_gate)
        {
            CompanionGroup group = _config.Groups.FirstOrDefault(g => g.Id == groupId);
            if (group == null || string.Equals(group.Name, name, StringComparison.Ordinal)) return;
            group.Name = name ?? string.Empty;
        }
        Persist();
    }

    public void SetPageFollow(Guid groupId, CompanionPageFollowMode mode)
    {
        lock (_gate)
        {
            CompanionGroup group = _config.Groups.FirstOrDefault(g => g.Id == groupId);
            if (group == null || group.PageFollow == mode) return;
            group.PageFollow = mode;
        }
        Persist();
    }

    public CompanionPageFollowMode GetPageFollow(string masterKey)
    {
        if (!IsMaster(masterKey) || IsPaused(masterKey)) return CompanionPageFollowMode.Off;

        CompanionPageFollowMode mode = FindGroup(masterKey)?.PageFollow ?? CompanionPageFollowMode.Off;
        return Enum.IsDefined(mode) ? mode : CompanionPageFollowMode.Off;
    }

    public bool TrySetMaster(Guid groupId, string deviceKey, out string error)
    {
        error = null;
        lock (_gate)
        {
            CompanionGroup group = _config.Groups.FirstOrDefault(g => g.Id == groupId);
            if (group == null) { error = Loc.Tr("Companions_ErrorGroupNotFound"); return false; }
            if (string.IsNullOrWhiteSpace(deviceKey)) { error = Loc.Tr("Companions_ErrorNoDevice"); return false; }
            if (KeyEquals(group.MasterDeviceKey, deviceKey)) return true;

            if (!CanJoinGroup(deviceKey))
            {
                error = NoSerialError(deviceKey);
                return false;
            }

            if (group.CompanionDeviceKeys.Contains(deviceKey, StringComparer.OrdinalIgnoreCase))
            {
                error = Loc.Tr("Companions_ErrorIsCompanionHere", GetDisplayName(deviceKey));
                return false;
            }

            CompanionGroup other = OwnerOf(deviceKey, exclude: group);
            if (other != null)
            {
                error = Loc.Tr("Companions_ErrorInOtherGroup", GetDisplayName(deviceKey), GroupLabel(other));
                return false;
            }

            group.MasterDeviceKey = deviceKey;
        }
        Persist();
        return true;
    }

    public bool TryAddCompanion(Guid groupId, string deviceKey, out string error)
    {
        error = null;
        lock (_gate)
        {
            CompanionGroup group = _config.Groups.FirstOrDefault(g => g.Id == groupId);
            if (group == null) { error = Loc.Tr("Companions_ErrorGroupNotFound"); return false; }
            if (string.IsNullOrWhiteSpace(deviceKey)) { error = Loc.Tr("Companions_ErrorNoDevice"); return false; }
            if (group.CompanionDeviceKeys.Contains(deviceKey, StringComparer.OrdinalIgnoreCase)) return true;

            if (!CanJoinGroup(deviceKey))
            {
                error = NoSerialError(deviceKey);
                return false;
            }

            if (KeyEquals(group.MasterDeviceKey, deviceKey))
            {
                error = Loc.Tr("Companions_ErrorIsMasterHere", GetDisplayName(deviceKey));
                return false;
            }

            CompanionGroup other = OwnerOf(deviceKey, exclude: group);
            if (other != null)
            {
                error = Loc.Tr("Companions_ErrorInOtherGroup", GetDisplayName(deviceKey), GroupLabel(other));
                return false;
            }

            group.CompanionDeviceKeys.Add(deviceKey);
        }
        Persist();
        return true;
    }

    public void RemoveCompanion(Guid groupId, string deviceKey)
    {
        bool removed;
        lock (_gate)
        {
            CompanionGroup group = _config.Groups.FirstOrDefault(g => g.Id == groupId);
            if (group == null) return;
            removed = group.CompanionDeviceKeys.RemoveAll(k => KeyEquals(k, deviceKey)) > 0;
        }
        if (removed) Persist();
    }

    private CompanionGroup OwnerOf(string deviceKey, CompanionGroup exclude) =>
        _config.Groups.FirstOrDefault(g =>
            !ReferenceEquals(g, exclude) &&
            (KeyEquals(g.MasterDeviceKey, deviceKey) ||
             g.CompanionDeviceKeys.Contains(deviceKey, StringComparer.OrdinalIgnoreCase)));

    public bool CanJoinGroup(string deviceKey) =>
        CompanionGroupValidator.IsDistinguishable(deviceKey, GetKnownDevices().Select(d => d.Key));

    private string NoSerialError(string deviceKey) =>
        Loc.Tr("Companions_ErrorNoSerial", GetDisplayName(deviceKey));

    private static string GroupLabel(CompanionGroup group) =>
        string.IsNullOrWhiteSpace(group.Name) ? Loc.Tr("Companions_UnnamedGroup") : group.Name;

    // ── Host tracking ───────────────────────────────────────────────────────

    private void OnHostAdded(DeviceHost host)
    {
        lock (_gate)
        {
            if (_connectHandlers.ContainsKey(host)) return;
            EventHandler handler = (_, _) => OnDeviceConnected(host);
            _connectHandlers[host] = handler;
            host.Controller.DeviceConnected += handler;
        }

        if (UpgradeSlugOnlyKeys([host.Device]))
            Persist();

        DeviceOnlineStateChanged?.Invoke(host.Device.ScopeKey);
    }

    /// <summary>
    /// Rewrites group members stored under a bare slug to the device's full key
    /// once its serial is known (a group created while the serial was not detected). Only done when
    /// exactly one known device of that model has a serial, so no member is guessed. True when
    /// anything changed.
    /// </summary>
    private bool UpgradeSlugOnlyKeys(IEnumerable<ResolvedDevice> devices)
    {
        Dictionary<string, string> fullKeyBySlug = devices
            .Where(d => !string.IsNullOrEmpty(SerialNormalizer.ForFilename(d.Serial)))
            .GroupBy(d => d.Slug, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(d => d.ScopeKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
            .ToDictionary(g => g.Key, g => g.First().ScopeKey, StringComparer.OrdinalIgnoreCase);
        if (fullKeyBySlug.Count == 0) return false;

        string Upgrade(string key) =>
            !string.IsNullOrWhiteSpace(key) && !CompanionGroupValidator.HasSerial(key) &&
            fullKeyBySlug.TryGetValue(key, out string full)
                ? full
                : key;

        bool changed = false;
        lock (_gate)
        {
            foreach (CompanionGroup group in _config.Groups)
            {
                string master = Upgrade(group.MasterDeviceKey);
                if (!KeyEquals(master, group.MasterDeviceKey)) { group.MasterDeviceKey = master; changed = true; }

                for (int i = 0; i < group.CompanionDeviceKeys.Count; i++)
                {
                    string companion = Upgrade(group.CompanionDeviceKeys[i]);
                    if (KeyEquals(companion, group.CompanionDeviceKeys[i])) continue;
                    group.CompanionDeviceKeys[i] = companion;
                    changed = true;
                }
            }

            if (changed)
                CompanionGroupValidator.Heal(_config.Groups);
        }

        return changed;
    }

    public void DeviceInitialized(DeviceHost host)
    {
        if (host == null) return;
        lock (_gate)
        {
            if (!_connectHandlers.ContainsKey(host) || !_initializedHosts.Add(host)) return;
        }

        RaiseDeviceReady(host);
    }

    public bool IsReady(DeviceHost host)
    {
        if (host == null) return false;
        lock (_gate) return _initializedHosts.Contains(host);
    }

    private void OnHostRemoved(DeviceHost host)
    {
        lock (_gate)
        {
            if (_connectHandlers.Remove(host, out EventHandler handler))
                host.Controller.DeviceConnected -= handler;
            _initializedHosts.Remove(host);
        }

        DeviceOnlineStateChanged?.Invoke(host.Device.ScopeKey);
    }

    private void OnDeviceConnected(DeviceHost host)
    {
        string key = host.Device.ScopeKey;
        DeviceOnlineStateChanged?.Invoke(key);

        // The first connect happens inside controller initialization, which still applies the
        // startup page afterwards; DeviceInitialized raises DeviceReady for that case instead.
        if (IsReady(host))
            RaiseDeviceReady(host);
    }

    private void RaiseDeviceReady(DeviceHost host)
    {
        // The controller raises DeviceConnected on the UI thread; a synchronous OnHostAdded
        // call may arrive elsewhere, so normalise.
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            DeviceReady?.Invoke(host);
        else
            Avalonia.Threading.Dispatcher.UIThread.Post(() => DeviceReady?.Invoke(host));
    }

    // ── Persistence ─────────────────────────────────────────────────────────

    private void Persist()
    {
        SaveQuietly();
        GroupsChanged?.Invoke();
    }

    private void SaveQuietly()
    {
        try
        {
            lock (_gate) _store.Save(_config);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Companions] Failed to save: {ex.Message}");
        }
    }

    private static bool KeyEquals(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
