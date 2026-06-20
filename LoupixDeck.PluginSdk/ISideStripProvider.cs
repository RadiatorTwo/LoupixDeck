namespace LoupixDeck.PluginSdk;

/// <summary>Which side display strip of a side-strip device (e.g. Razer Stream
/// Controller) a session drives.</summary>
public enum StripSide
{
    Left,
    Right
}

/// <summary>Vertical swipe direction on a side strip.</summary>
public enum StripSwipeDirection
{
    Up,
    Down
}

/// <summary>
/// One of the dials adjacent to the strip a session drives (3 per side on the Razer).
/// Lets a provider reflect what each knob actually controls — e.g. parse a device id
/// out of <see cref="RightCommand"/> to show that endpoint's level. Indices are
/// 0-based within the side.
/// </summary>
public sealed class SideStripRotary
{
    public int Index { get; init; }

    /// <summary>Static label configured for the knob (may be empty).</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Command run on counter-clockwise / left turn (may be empty).</summary>
    public string LeftCommand { get; init; } = string.Empty;

    /// <summary>Command run on clockwise / right turn (may be empty).</summary>
    public string RightCommand { get; init; } = string.Empty;

    /// <summary>Command run on knob press (may be empty).</summary>
    public string PressCommand { get; init; } = string.Empty;
}

/// <summary>
/// Handed to <see cref="ISideStripProvider.CreateSession"/>. Carries the strip geometry,
/// the adjacent dials' bindings, and side-bound paging callbacks so a session can page
/// its own column (e.g. on a swipe) without tracking device geometry.
/// </summary>
public sealed class SideStripContext
{
    /// <summary>The strip this session drives.</summary>
    public StripSide Side { get; init; }

    /// <summary>Strip width in device pixels (60 on the Razer).</summary>
    public int Width { get; init; }

    /// <summary>Strip height in device pixels (270 on the Razer).</summary>
    public int Height { get; init; }

    /// <summary>The dials adjacent to this strip on the current page (3 on the Razer),
    /// in top-to-bottom order.</summary>
    public IReadOnlyList<SideStripRotary> Rotaries { get; init; } = Array.Empty<SideStripRotary>();

    /// <summary>Advances this side's rotary page. Safe to call from any thread.</summary>
    public Action RequestNextPage { get; init; } = static () => { };

    /// <summary>Goes back one rotary page on this side. Safe to call from any thread.</summary>
    public Action RequestPreviousPage { get; init; } = static () => { };
}

/// <summary>
/// Plugin-supplied, discoverable renderer for a side display strip. Bound per rotary
/// page (the user picks it in the strip-mode editor when the strip is in plugin-override
/// mode). The provider itself is a stateless descriptor + factory: the host calls
/// <see cref="CreateSession"/> once per attachment, so the same provider can drive both
/// the left and right strip at the same time, each with its own session and context.
/// Unlike <see cref="IExclusiveModeProvider"/>, the rest of the device keeps working —
/// only the 60×270 strip region is owned, and paging continues to function.
/// </summary>
public interface ISideStripProvider
{
    /// <summary>Stable, unique id persisted in the page binding. Recommended form:
    /// <c>"{pluginId}.{name}"</c> to avoid collisions across plugins.</summary>
    string Id { get; }

    /// <summary>Human-readable label shown in the strip-mode editor's provider picker.</summary>
    string Title { get; }

    /// <summary>
    /// Creates a live session for one strip attachment. Called when a page bound to this
    /// provider becomes current on a side; the returned session is disposed on detach
    /// (navigating away, full-device takeover, device-off, or plugin unload).
    /// </summary>
    ISideStripSession CreateSession(SideStripContext context);
}

/// <summary>
/// One live attachment of an <see cref="ISideStripProvider"/> to a single side. Holds the
/// per-side state (geometry, the dials' bindings, subscriptions). Disposed by the host
/// on detach — release timers / event subscriptions in <see cref="IDisposable.Dispose"/>.
/// </summary>
public interface ISideStripSession : IDisposable
{
    /// <summary>
    /// Draws the whole strip (the context's width × height, i.e. 60×270 on the Razer) onto the
    /// host-provided <paramref name="canvas"/> using its primitives. Return <c>true</c> when the
    /// strip was drawn, or <c>false</c> to let the host fall back to the default segmented
    /// dial-label rendering for that frame.
    /// </summary>
    bool RenderStrip(IRenderCanvas canvas);

    /// <summary>Raised when <see cref="RenderStrip"/>'s output has changed and the host
    /// should redraw. May be raised from a background thread; the host serializes and
    /// rate-limits the resulting redraws.</summary>
    event EventHandler StripChanged;

    /// <summary>A tap landed on the strip. Coordinates are strip-local (0..Width, 0..Height).</summary>
    void OnStripTapped(int x, int y);

    /// <summary>A vertical swipe occurred on the strip. The session decides whether to page
    /// (via <see cref="SideStripContext.RequestNextPage"/> /
    /// <see cref="SideStripContext.RequestPreviousPage"/>) or to consume it.</summary>
    void OnStripSwiped(StripSwipeDirection direction);
}

/// <summary>
/// Marker for an <see cref="ISideStripProvider"/> that can render <b>individual segments</b>
/// of a strip in the host's <i>segmented</i> mode (vs owning the whole strip in plugin-override
/// mode). In segmented mode the host renders each dial's segment itself, but offers a
/// segment-capable provider the chance to draw any segment it owns (e.g. the volume bar of an
/// audio dial); segments the provider declines fall back to the default dial label. The session
/// the provider returns must also implement <see cref="ISegmentStripSession"/>.
/// </summary>
public interface ISegmentStripProvider : ISideStripProvider
{
}

/// <summary>
/// Per-segment rendering capability, implemented by the session of an
/// <see cref="ISegmentStripProvider"/> alongside <see cref="ISideStripSession"/>. Lets a plugin
/// draw a single strip segment in segmented mode while the host keeps ownership of the others.
/// Live updates, taps and disposal reuse the <see cref="ISideStripSession"/> members
/// (<see cref="ISideStripSession.StripChanged"/>, <see cref="ISideStripSession.OnStripTapped"/>).
/// </summary>
public interface ISegmentStripSession
{
    /// <summary>
    /// Draws one segment onto the host-provided <paramref name="canvas"/> (sized to the strip
    /// width × the segment height, i.e. 60×90 on the Razer), using its primitives. Return
    /// <c>true</c> when the segment was drawn, or <c>false</c> to let the host draw that dial's
    /// default label. <paramref name="rotaryIndex"/> is 0-based within the side, top-to-bottom,
    /// matching <see cref="SideStripContext.Rotaries"/>.
    /// </summary>
    bool RenderSegment(int rotaryIndex, IRenderCanvas canvas);
}
