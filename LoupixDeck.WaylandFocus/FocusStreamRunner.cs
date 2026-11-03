namespace LoupixDeck.WaylandFocus;

/// <summary>
/// Runs an <see cref="IFocusSource"/> as the interface promises: a stream that never
/// completes. The sources themselves do not all keep that promise — hyprland and niri
/// end their enumerations when the IPC socket closes (a compositor restart, a rotated
/// socket), and COSMIC/sway die with an exception instead — so both shapes are handled
/// here, at one place:
/// <list type="bullet">
/// <item>A source that never produced its initial element failed at setup: a stale
/// socket, a compositor without the focus protocol, a wedged handshake. Reconnecting
/// cannot heal those, so the runner throws <see cref="FocusStreamFailedException"/>
/// immediately and the caller takes its fallback.</item>
/// <item>A source that had been working is treated as a transient death: the end is
/// logged with its reason and the source is rebuilt with a capped exponential backoff.
/// When the budget runs out the same exception is thrown, and cancellation propagates
/// untouched as the only clean way out.</item>
/// </list>
/// </summary>
public sealed class FocusStreamRunner
{
    // First retry after one second, doubling to a sixteen-second ceiling: the five
    // attempts span about half a minute - long enough to ride out a compositor
    // restart, short enough that a waiting fallback is not held up noticeably.
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(16);
    private const int DefaultMaxAttempts = 5;

    private readonly Func<IFocusSource> _sourceFactory;
    private readonly Action<string>? _log;
    private readonly int _maxAttempts;
    private readonly Func<int, TimeSpan> _backoff;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <summary>Production wiring: real backoff delays.</summary>
    public FocusStreamRunner(Func<IFocusSource> sourceFactory, Action<string>? log = null)
        : this(sourceFactory, log, DefaultMaxAttempts,
            attempt => TimeSpan.FromSeconds(
                Math.Min(MaxBackoff.TotalSeconds, 1 << (attempt - 1))),
            Task.Delay)
    {
    }

    /// <summary>Test seam: injectable attempt cap, backoff schedule and delay.</summary>
    internal FocusStreamRunner(Func<IFocusSource> sourceFactory, Action<string>? log,
        int maxAttempts, Func<int, TimeSpan> backoff,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        _sourceFactory = sourceFactory;
        _log = log;
        _maxAttempts = maxAttempts;
        _backoff = backoff;
        _delay = delay;
    }

    /// <summary>
    /// Forwards focus events until cancelled. Throws <see cref="FocusStreamFailedException"/>
    /// when the stream cannot start or does not come back; an
    /// <see cref="OperationCanceledException"/> means the caller's token fired.
    /// </summary>
    public async Task RunAsync(Action<FocusInfo?> onEvent, CancellationToken cancellationToken)
    {
        int attempt = 0;

        while (true)
        {
            IFocusSource source;
            try
            {
                source = _sourceFactory();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new FocusStreamFailedException(
                    $"the focus source could not be created: {ex.Message}", ex);
            }

            _log?.Invoke(attempt == 0
                ? $"Wayland monitor active (compositor: {source.Compositor})."
                : $"Wayland monitor reconnected (compositor: {source.Compositor}).");

            Exception? death = null;
            bool yielded = false;

            try
            {
                await foreach (FocusInfo? focus in source.WatchAsync(cancellationToken))
                {
                    yielded = true;
                    onEvent(focus);
                }
            }
            catch (OperationCanceledException)
            {
                throw; // shutdown through the caller's token — the clean exit
            }
            catch (Exception ex)
            {
                death = ex; // classified below by whether anything was ever yielded
            }

            if (!yielded)
            {
                // The initial element never arrived: stale socket, missing protocol or a
                // wedged handshake - none of which a reconnect heals, so fail fast and
                // let the caller take its fallback instead of burning the backoff budget.
                throw new FocusStreamFailedException(
                    death is null
                        ? "the source ended before reporting the current focus"
                        : $"the focus stream could not start: {death.Message}",
                    death);
            }

            // Had been working, so a death is transient in comparison. Normal completion
            // (hyprland/niri seeing their socket close) lands here with death == null.
            if (attempt >= _maxAttempts)
            {
                throw new FocusStreamFailedException(
                    $"the focus stream did not come back after {_maxAttempts} attempts",
                    death);
            }

            attempt++;
            TimeSpan wait = _backoff(attempt);
            _log?.Invoke(
                $"Wayland focus stream ended ({(death?.Message ?? "the compositor closed the connection")}); " +
                $"reconnecting in {wait.TotalSeconds:0.#}s (attempt {attempt}/{_maxAttempts}).");

            await _delay(wait, cancellationToken); // OCE during shutdown propagates
        }
    }
}

/// <summary>
/// The focus stream cannot run: setup never succeeded, or a working stream died and did
/// not come back within the runner's backoff budget. The monitor maps this to its xprop
/// fallback with a single logged line.
/// </summary>
public sealed class FocusStreamFailedException : Exception
{
    public FocusStreamFailedException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}