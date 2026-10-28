namespace LoupixDeck.WaylandFocus;

/// <summary>A source of focus-change events for one compositor.</summary>
public interface IFocusSource
{
    /// <summary>Short compositor name, e.g. "cosmic".</summary>
    string Compositor { get; }

    /// <summary>
    /// Emits the current focus as the first element, then a new element
    /// whenever the focused toplevel changes. <see langword="null"/> means
    /// no toplevel has keyboard focus (e.g. a layer-shell surface does).
    /// The stream never completes; it only ends via cancellation.
    /// </summary>
    IAsyncEnumerable<FocusInfo?> WatchAsync(CancellationToken cancellationToken = default);
}