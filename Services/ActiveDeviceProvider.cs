namespace LoupixDeck.Services;

/// <summary>
/// Root-level holder for the active device's child <see cref="IServiceProvider"/>.
///
/// Phase 1 (issue #116) splits DI into a device-agnostic root container plus one
/// child provider per device. Root-resident services that must reach device-bound
/// services (notably the plugin host delegates built in <c>PluginManager</c>)
/// resolve them through <see cref="Current"/> instead of capturing the root
/// provider. With one device <see cref="Current"/> is set once after the device
/// provider is built; Phase 2 turns this into a registry of device providers and
/// resolves the originating device per call.
/// </summary>
public interface IActiveDeviceProvider
{
    /// <summary>The active device's service provider. Set during startup before
    /// plugins load or any command executes; null until then.</summary>
    IServiceProvider Current { get; set; }
}

public sealed class ActiveDeviceProvider : IActiveDeviceProvider
{
    public IServiceProvider Current { get; set; }
}
