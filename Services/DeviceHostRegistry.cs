using LoupixDeck.Controllers;
using LoupixDeck.Registry;

namespace LoupixDeck.Services;

/// <summary>
/// One running device: its resolved identity, its child <see cref="IServiceProvider"/>,
/// its controller, and whether it owns the (single) config window.
/// </summary>
public sealed record DeviceHost(
    ResolvedDevice Device,
    IServiceProvider Provider,
    IDeviceController Controller,
    bool IsPrimary);

/// <summary>
/// Root-level registry of every device brought up this session (issue #116 phase 2).
/// Lets process-wide concerns (e.g. quit → shut down every device's plugins) reach
/// all devices, and is the seam phase 3 builds on (UI enumeration, CLI serial routing).
/// </summary>
public interface IDeviceHostRegistry
{
    IReadOnlyList<DeviceHost> Hosts { get; }

    /// <summary>The device that owns the config window (CLI/UI default target).</summary>
    DeviceHost Primary { get; }

    void Add(DeviceHost host);

    /// <summary>Drop a host (hot-unplug). No-op if it isn't registered.</summary>
    void Remove(DeviceHost host);

    /// <summary>
    /// Resolve a host from a CLI/user selector matched case-insensitively against
    /// its scope key (slug+serial), bare serial, or slug. Returns null when nothing
    /// matches; an ambiguous slug match (two identical units) yields the first.
    /// </summary>
    DeviceHost Find(string selector);
}

public sealed class DeviceHostRegistry : IDeviceHostRegistry
{
    private readonly List<DeviceHost> _hosts = [];
    private readonly object _gate = new();

    public IReadOnlyList<DeviceHost> Hosts
    {
        get { lock (_gate) return _hosts.ToArray(); }
    }

    public DeviceHost Primary
    {
        get { lock (_gate) return _hosts.FirstOrDefault(h => h.IsPrimary) ?? _hosts.FirstOrDefault(); }
    }

    public void Add(DeviceHost host)
    {
        if (host == null) return;
        lock (_gate) _hosts.Add(host);
    }

    public void Remove(DeviceHost host)
    {
        if (host == null) return;
        lock (_gate) _hosts.Remove(host);
    }

    public DeviceHost Find(string selector)
    {
        if (string.IsNullOrWhiteSpace(selector)) return null;
        selector = selector.Trim();
        lock (_gate)
        {
            return _hosts.FirstOrDefault(h =>
                       string.Equals(h.Device.ScopeKey, selector, StringComparison.OrdinalIgnoreCase))
                   ?? _hosts.FirstOrDefault(h =>
                       !string.IsNullOrEmpty(h.Device.Serial) &&
                       string.Equals(h.Device.Serial, selector, StringComparison.OrdinalIgnoreCase))
                   ?? _hosts.FirstOrDefault(h =>
                       string.Equals(h.Device.Slug, selector, StringComparison.OrdinalIgnoreCase));
        }
    }
}
