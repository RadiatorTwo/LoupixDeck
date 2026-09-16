using LoupixDeck.Models;
using LoupixDeck.Services;
using LoupixDeck.Services.Plugins;
using LoupixDeck.ViewModels.Base;
using Microsoft.Extensions.DependencyInjection;

namespace LoupixDeck.ViewModels.Plugins;

/// <summary>
/// One running device, as the installed-plugins page sees it.
///
/// Plugins are loaded once for the whole process, but whether a plugin is <em>enabled</em> is
/// per device: it lives in that device's LoupedeckConfig.EnabledPlugins, and PluginManager
/// loads a plugin as soon as any running device enables it. The reload coordinator and the
/// installer are device-scoped services for the same reason.
///
/// So this window, which is device-independent, has to name the device it is acting on and
/// resolve those services from that device's container rather than silently using the primary
/// one.
/// </summary>
public sealed class PluginDeviceViewModel(DeviceHost host, Func<string> describe) : ViewModelBase
{
    public DeviceHost Host { get; } = host;

    public string ScopeKey => Host.Device.ScopeKey;

    /// <summary>Model name, with a trimmed serial while a second unit of the same model runs.</summary>
    public string Name => describe();

    public LoupedeckConfig Config => Host.Provider.GetService<LoupedeckConfig>();

    public IPluginReloadService Reload => Host.Provider.GetService<IPluginReloadService>();

    public override string ToString() => Name;
}
