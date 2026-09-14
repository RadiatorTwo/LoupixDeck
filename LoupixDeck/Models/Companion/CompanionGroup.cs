namespace LoupixDeck.Models.Companion;

/// <summary>
/// One master with the companions it controls. Devices are referenced by their scope key
/// (<see cref="LoupixDeck.Registry.ResolvedDevice.ScopeKey"/>: slug plus serial), the same token
/// that names the device's config file, so a group survives restarts and unplugged devices.
/// A group is always exactly one level deep: a master and its companions, nothing nested.
/// </summary>
public sealed class CompanionGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>Scope key of the master device. Empty while the group is still being set up.</summary>
    public string MasterDeviceKey { get; set; } = string.Empty;

    /// <summary>Scope keys of the companion devices, in display order.</summary>
    public List<string> CompanionDeviceKeys { get; set; } = [];
}
