namespace LoupixDeck.WaylandFocus.Sources;

/// <summary>
/// Bounds a source's setup phase (connect → first reliable state).
/// <para>
/// Why this exists: compositor IPC sockets happily accept a connection
/// even when the session is wedged or a stale socket file was left behind
/// by a crashed compositor. Without a deadline the watcher would sit in
/// its handshake forever — looking alive while emitting nothing. Expiry
/// surfaces as <see cref="TimeoutException"/>, a loud typed failure,
/// distinct from the caller's own cancellation.
/// </para>
/// </summary>
internal sealed class SetupPhase : IDisposable
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private readonly CancellationTokenSource _linked;
    private readonly CancellationToken _caller;
    private readonly string _what;
    private readonly TimeSpan _timeout;

    public SetupPhase(string what, TimeSpan? timeout, CancellationToken caller)
    {
        _what = what;
        _timeout = timeout ?? DefaultTimeout;
        _caller = caller;
        _linked = CancellationTokenSource.CreateLinkedTokenSource(caller);
        _linked.CancelAfter(_timeout);
    }

    /// <summary>Aborts on either timeout or caller cancellation; pass to setup reads.</summary>
    public CancellationToken Token => _linked.Token;

    /// <summary>
    /// Converts a cancellation observed during setup into the right error:
    /// expiry becomes <see cref="TimeoutException"/>, but the caller's own
    /// cancellation must stay cancellation so Ctrl+C keeps working.
    /// </summary>
    public Exception MapCancellation(Exception error) =>
        error is OperationCanceledException && !_caller.IsCancellationRequested
            ? new TimeoutException(
                $"{_what} did not complete within {_timeout.TotalSeconds:0.##} seconds")
            : error;

    public void Dispose() => _linked.Dispose();
}