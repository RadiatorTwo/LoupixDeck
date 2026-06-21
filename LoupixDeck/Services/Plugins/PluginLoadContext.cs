#nullable enable
using System.Reflection;
using System.Runtime.Loader;

namespace LoupixDeck.Services.Plugins;

/// <summary>
/// Isolated, collectible load context for one plugin. Each plugin gets its own
/// context so plugins can carry their own versions of shared dependencies and
/// can later be unloaded/hot-reloaded.
/// </summary>
/// <remarks>
/// The SDK assembly (and anything already present in the default context) is
/// deliberately NOT loaded here — returning <see langword="null"/> from <see cref="Load"/>
/// makes the runtime resolve it from the default context, so contract types
/// such as <c>IPluginCommand</c> are identical on both sides of the boundary.
/// </remarks>
internal sealed class PluginLoadContext(string pluginMainAssemblyPath) : AssemblyLoadContext(isCollectible: true)
{
    private readonly AssemblyDependencyResolver resolver = new(pluginMainAssemblyPath);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Share the SDK from the default context — never load a private copy,
        // or the plugin's IPluginCommand would be a different Type.
        if (assemblyName.Name == "LoupixDeck.PluginSdk")
            return null;

        var path = resolver.ResolveAssemblyToPath(assemblyName);
        return path != null ? LoadFromAssemblyPath(path) : null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path != null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
    }
}
