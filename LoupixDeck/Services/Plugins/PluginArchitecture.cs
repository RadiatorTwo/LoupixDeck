using System.Runtime.InteropServices;

namespace LoupixDeck.Services.Plugins;

/// <summary>
/// CPU architectures a plugin build supports, written as in .NET runtime identifiers (<c>x64</c>,
/// <c>arm64</c>). A plugin loads into this process, so <see cref="RuntimeInformation.ProcessArchitecture"/>
/// decides: an x64 build running emulated on an ARM64 machine still takes x64 plugins.
/// </summary>
public static class PluginArchitecture
{
    /// <summary>The architecture of this process: <c>x64</c>, <c>arm64</c>, <c>x86</c> or <c>arm</c>.</summary>
    public static string Current { get; } = Name(RuntimeInformation.ProcessArchitecture);

    /// <summary>The runtime-identifier name of <paramref name="architecture"/>.</summary>
    public static string Name(Architecture architecture) => architecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "x86",
        Architecture.Arm => "arm",
        _ => architecture.ToString().ToLowerInvariant()
    };

    /// <summary>
    /// True when <paramref name="architectures"/> allows <paramref name="architecture"/>. A missing or
    /// empty list allows every architecture, which is what every plugin published before the list existed means.
    /// </summary>
    public static bool Allows(IReadOnlyCollection<string> architectures, string architecture)
    {
        return architectures is not { Count: > 0 }
               || architectures.Any(a => string.Equals(a?.Trim(), architecture, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary><see cref="Allows(IReadOnlyCollection{string}, string)"/> for this process.</summary>
    public static bool AllowsCurrent(IReadOnlyCollection<string> architectures) => Allows(architectures, Current);
}
