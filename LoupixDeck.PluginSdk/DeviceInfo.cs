namespace LoupixDeck.PluginSdk;

/// <summary>
/// Read-only description of the device the host is currently driving. Passed to
/// plugins so a command can adapt to the active hardware without referencing
/// any core type.
/// </summary>
public sealed record DeviceInfo(string Name, string VendorId, string ProductId, string Slug);
