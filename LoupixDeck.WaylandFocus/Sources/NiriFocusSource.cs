using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace LoupixDeck.WaylandFocus.Sources;

/// <summary>Focus source for niri, driven by its JSON event stream.</summary>
public sealed class NiriFocusSource(TimeSpan? setupTimeout = null) : IFocusSource
{
    public string Compositor => "niri";

    public async IAsyncEnumerable<FocusInfo?> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var socketPath = Environment.GetEnvironmentVariable("NIRI_SOCKET")
            ?? throw new InvalidOperationException("NIRI_SOCKET is not set (are we inside a niri session?)");

        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        socket.Connect(new UnixDomainSocketEndPoint(socketPath));
        await using var stream = new NetworkStream(socket, ownsSocket: true);

        // One request, then newline-delimited JSON events for the connection's lifetime.
        await stream.WriteAsync("\"EventStream\"\n"u8.ToArray(), cancellationToken);
        using var reader = new StreamReader(stream);

        // The stream opens with the full window state, so the first line
        // doubles as setup — bound it, or a wedged niri hangs us silently.
        string? pending;
        using (var setup = new SetupPhase(
                   "niri's initial window state", setupTimeout, cancellationToken))
        {
            try
            {
                pending = await reader.ReadLineAsync(setup.Token);
            }
            catch (OperationCanceledException ex)
            {
                throw setup.MapCancellation(ex);
            }
        }

        if (pending is null)
            throw new EndOfStreamException("niri closed the event stream immediately");

        var processor = new NiriEventProcessor();
        var emittedInitial = false;

        while (pending is not null)
        {
            // Decode failures drop the event, never the stream.
            FocusInfo? focus = null;
            var changed = false;
            try
            {
                changed = processor.Process(pending, out focus);
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException
                or InvalidOperationException or KeyNotFoundException)
            {
                Console.Error.WriteLine($"[focus-watcher] niri: dropped malformed event: {ex.Message}");
            }

            if (changed)
            {
                emittedInitial = true;
                yield return focus;
            }
            else if (!emittedInitial)
            {
                // Even an eventless first line carries state: report the
                // initial (possibly empty) focus exactly once.
                emittedInitial = true;
                yield return processor.CurrentFocus();
            }

            pending = await ReadLineOrEndAsync(reader, cancellationToken);
        }
    }

    /// <summary>A broken stream ends the watch; it does not crash it.</summary>
    private static async Task<string?> ReadLineOrEndAsync(StreamReader reader, CancellationToken ct)
    {
        try
        {
            return await reader.ReadLineAsync(ct);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            Console.Error.WriteLine($"[focus-watcher] niri: event stream failed: {ex.Message}");
            return null;
        }
    }
}