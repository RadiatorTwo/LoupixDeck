using System.Reflection;
using System.Runtime.Loader;

namespace LoupixDeck.Services.Plugins;

/// <summary>
/// Isolated, collectible load context for one plugin. Each plugin gets its own
/// context so plugins can carry their own versions of shared dependencies and
/// can later be unloaded/hot-reloaded.
///
/// The SDK assembly (and anything already present in the default context) is
/// deliberately NOT loaded here — returning <c>null</c> from <see cref="Load"/>
/// makes the runtime resolve it from the default context, so contract types
/// such as <c>IPluginCommand</c> are identical on both sides of the boundary.
/// </summary>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginMainAssemblyPath)
        : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginMainAssemblyPath);
    }

    protected override Assembly Load(AssemblyName assemblyName)
    {
        // Share the SDK from the default context — never load a private copy,
        // or the plugin's IPluginCommand would be a different Type.
        if (assemblyName.Name == "LoupixDeck.PluginSdk")
            return null;

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path != null ? LoadFromAssemblyPath(path) : null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path != null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
    }
}
