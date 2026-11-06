using System.Diagnostics.CodeAnalysis;
using LoupixDeck.WaylandFocus.Activation;
using LoupixDeck.WaylandFocus.Sources;

namespace LoupixDeck.WaylandFocus;

/// <summary>Figures out which compositor runs the current session.</summary>
public static class CompositorDetector
{
    /// <summary>Creates the focus source for the detected compositor.</summary>
    public static IFocusSource Create()
    {
        string name = Detect();
        return name switch
        {
            "cosmic" => new CosmicFocusSource(),
            "sway" => new SwayFocusSource(),
            "niri" => new NiriFocusSource(),
            "hyprland" => new HyprlandFocusSource(),
            _ => throw new NotSupportedException(
                $"unsupported compositor '{name}' (supported: cosmic, sway, niri, hyprland)"),
        };
    }

    /// <summary>
    /// Creates the window activator for the detected compositor, or null on desktops without a
    /// supported focus IPC.
    /// </summary>
    public static IWindowActivator? CreateActivator()
    {
        if (!TryDetect(out string? name))
            return null;

        return name switch
        {
            "cosmic" => new CosmicWindowActivator(),
            "sway" => new SwayWindowActivator(),
            "niri" => new NiriWindowActivator(),
            "hyprland" => new HyprlandWindowActivator(),
            _ => null,
        };
    }

    /// <summary>Detects the compositor from environment signals; throws when none is supported.</summary>
    public static string Detect()
    {
        if (TryDetect(out string? name))
            return name;

        throw new NotSupportedException(
            "could not detect the compositor (supported: cosmic, sway, niri, hyprland)");
    }

    /// <summary>
    /// Non-throwing <see cref="Detect"/>: <c>false</c> on desktops that expose no
    /// focus IPC we support (GNOME, KDE, …), letting the caller fall back instead of erroring.
    /// An unreadable runtime directory counts as "no compositor" — IPC could not
    /// connect through it anyway.
    /// </summary>
    public static bool TryDetect([NotNullWhen(true)] out string? name)
    {
        try
        {
            // Environment variables owned by the compositor itself: the most reliable signal.
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NIRI_SOCKET")))
            {
                name = "niri";
                return true;
            }
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE")))
            {
                name = "hyprland";
                return true;
            }
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SWAYSOCK")))
            {
                name = "sway";
                return true;
            }

            // Sway leaves its IPC socket in the runtime dir even when SWAYSOCK is unset.
            string? runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if (runtimeDir is not null && Directory.GetFiles(runtimeDir, "sway-ipc.*.sock").Length > 0)
            {
                name = "sway";
                return true;
            }

            string desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "";
            if (desktop.Contains("cosmic", StringComparison.OrdinalIgnoreCase))
            {
                name = "cosmic";
                return true;
            }

            name = null;
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The compositor's sockets live in the runtime dir; if it cannot be
            // read, Wayland IPC cannot connect either, so report "no compositor"
            // and let the caller fall back to xprop.
            name = null;
            return false;
        }
    }
}