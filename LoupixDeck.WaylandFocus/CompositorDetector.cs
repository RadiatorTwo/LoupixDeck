using LoupixDeck.WaylandFocus.Sources;

namespace LoupixDeck.WaylandFocus;

/// <summary>Figures out which compositor runs the current session.</summary>
public static class CompositorDetector
{
    /// <summary>Creates the focus source for the detected (or forced) compositor.</summary>
    public static IFocusSource Create(string? forced = null, TextWriter? trace = null)
    {
        var name = forced?.ToLowerInvariant() ?? Detect();
        return name switch
        {
            "cosmic" => new CosmicFocusSource(trace: trace),
            "sway" => new SwayFocusSource(),
            "niri" => new NiriFocusSource(),
            "hyprland" => new HyprlandFocusSource(),
            _ => throw new NotSupportedException(
                $"unsupported compositor '{name}' (supported: cosmic, sway, niri, hyprland)"),
        };
    }

    /// <summary>Detects the compositor from environment signals.</summary>
    public static string Detect()
    {
        // Environment variables owned by the compositor itself: the most reliable signal.
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NIRI_SOCKET")))
            return "niri";
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE")))
            return "hyprland";
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SWAYSOCK")))
            return "sway";

        // Sway leaves its IPC socket in the runtime dir even when SWAYSOCK is unset.
        var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (runtimeDir is not null && Directory.GetFiles(runtimeDir, "sway-ipc.*.sock").Length > 0)
            return "sway";

        var desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "";
        if (desktop.Contains("cosmic", StringComparison.OrdinalIgnoreCase))
            return "cosmic";

        throw new NotSupportedException(
            "could not detect the compositor; pass --compositor <cosmic|sway|niri|hyprland>");
    }
}