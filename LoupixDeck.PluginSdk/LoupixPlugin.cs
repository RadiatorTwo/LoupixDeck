namespace LoupixDeck.PluginSdk;

/// <summary>
/// The entry point of a LoupixDeck plugin. Each plugin assembly provides
/// exactly one concrete subclass; the host discovers it, instantiates it,
/// calls <see cref="Initialize"/> once, collects its commands, and calls
/// <see cref="Shutdown"/> on unload or app exit.
/// </summary>
public abstract class LoupixPlugin
{
    /// <summary>Identity and versioning of this plugin.</summary>
    public abstract PluginMetadata Metadata { get; }

    /// <summary>Called once after load, before commands are collected. The
    /// host hands in the bridge the plugin keeps for its lifetime.</summary>
    public virtual void Initialize(IPluginHost host)
    {
    }

    /// <summary>Called on plugin unload or application exit. Release resources,
    /// stop background work, close connections.</summary>
    public virtual void Shutdown()
    {
    }

    /// <summary>Returns the commands this plugin contributes.</summary>
    public abstract IEnumerable<IPluginCommand> GetCommands();

    /// <summary>
    /// Returns the side-strip providers this plugin contributes (renderers a user can
    /// bind to a Razer side display strip in plugin-override mode). The host collects
    /// these after <see cref="Initialize"/>, alongside <see cref="GetCommands"/>.
    /// Defaults to none.
    /// </summary>
    public virtual IEnumerable<ISideStripProvider> GetSideStripProviders() =>
        Array.Empty<ISideStripProvider>();
}
