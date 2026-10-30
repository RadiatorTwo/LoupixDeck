namespace LoupixDeck.WaylandFocus;

/// <summary>
/// Normalized snapshot of the currently focused toplevel, independent of
/// which compositor reported it.
/// </summary>
/// <param name="AppId">The Wayland app_id of the focused window, when known.</param>
/// <param name="Title">The window title, when known.</param>
/// <param name="Pid">The owning process id, when known.</param>
/// <param name="ProcessName">
/// The OS process name resolved from <paramref name="Pid"/> (or matched from
/// <paramref name="AppId"/> when the compositor does not expose PIDs, e.g.
/// COSMIC). Enriched onto the event before it is emitted, so downstream
/// event processors can compare on a stable process identity.
/// </param>
public sealed record FocusInfo(
    string? AppId,
    string? Title,
    int? Pid,
    string? ProcessName = null);