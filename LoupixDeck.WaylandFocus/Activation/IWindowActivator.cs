namespace LoupixDeck.WaylandFocus.Activation;

/// <summary>One toplevel window as a compositor lists it.</summary>
/// <param name="AppId">The Wayland app_id, or the X11 window class for XWayland windows.</param>
/// <param name="Title">The window title, when known.</param>
/// <param name="Pid">The owning process id, when the compositor exposes it (not COSMIC).</param>
/// <param name="IsFocused">True for the window that holds keyboard focus.</param>
public sealed record ToplevelWindow(string? AppId, string? Title, int? Pid, bool IsFocused);

/// <summary>What <see cref="IWindowActivator.ActivateAsync"/> found and did.</summary>
public enum WindowActivationResult
{
    /// <summary>No window matched; nothing was done.</summary>
    NoMatch,

    /// <summary>A matching window already holds focus; nothing was done.</summary>
    AlreadyFocused,

    /// <summary>A matching window was asked to take focus.</summary>
    Activated
}

/// <summary>
/// Focuses a window through the compositor's own IPC — the counterpart to
/// <see cref="IFocusSource"/>, which only watches focus. Listing and activating happen in one
/// call because some protocols (COSMIC) hand out window handles that are only valid on the
/// connection that listed them.
/// </summary>
public interface IWindowActivator
{
    /// <summary>Short compositor name, e.g. "sway".</summary>
    string Compositor { get; }

    /// <summary>
    /// Lists the toplevel windows and focuses the first one <paramref name="match"/> accepts,
    /// unless a matching window already has focus.
    /// </summary>
    Task<WindowActivationResult> ActivateAsync(Func<ToplevelWindow, bool> match, CancellationToken cancellationToken);
}

/// <summary>Shared selection rule of every <see cref="IWindowActivator"/>.</summary>
public static class WindowPicker
{
    /// <summary>
    /// The window to activate among <paramref name="windows"/>, in the compositor's order. Null
    /// with <see cref="WindowActivationResult.AlreadyFocused"/> when a match already has focus.
    /// </summary>
    public static (WindowActivationResult Result, T? Window) Pick<T>(
        IEnumerable<(ToplevelWindow Info, T Handle)> windows, Func<ToplevelWindow, bool> match)
        where T : class
    {
        T? first = null;
        foreach ((ToplevelWindow info, T handle) in windows)
        {
            if (!match(info))
                continue;

            if (info.IsFocused)
                return (WindowActivationResult.AlreadyFocused, null);

            first ??= handle;
        }

        return first is null
            ? (WindowActivationResult.NoMatch, null)
            : (WindowActivationResult.Activated, first);
    }
}
