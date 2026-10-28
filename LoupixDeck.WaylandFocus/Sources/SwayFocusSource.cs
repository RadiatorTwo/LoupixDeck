using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using LoupixDeck.WaylandFocus.Ipc;

namespace LoupixDeck.WaylandFocus.Sources;

/// <summary>Focus source for sway (and other i3-IPC compatible compositors).</summary>
public sealed class SwayFocusSource(TimeSpan? setupTimeout = null) : IFocusSource
{
    public string Compositor => "sway";

    public async IAsyncEnumerable<FocusInfo?> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")
            ?? throw new InvalidOperationException("XDG_RUNTIME_DIR is not set");
        var socketPath = Environment.GetEnvironmentVariable("SWAYSOCK")
            // Several sessions can leave sockets behind; the freshest one
            // belongs to the live session.
            ?? Directory.GetFiles(runtimeDir, "sway-ipc.*.sock")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
            ?? throw new InvalidOperationException("no sway IPC socket found");

        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        socket.Connect(new UnixDomainSocketEndPoint(socketPath));
        using var stream = new NetworkStream(socket, ownsSocket: true);

        // Setup (connect -> first reply) is bounded: a wedged IPC socket
        // must fail loudly, not hang the watcher.
        FocusInfo? initial;
        using (var setup = new SetupPhase(
                   "sway's initial tree request", setupTimeout, cancellationToken))
        {
            try
            {
                // Initial state: walk the window tree to the node with "focused": true.
                await stream.WriteAsync(
                    SwayIpcProtocol.Encode(SwayIpcProtocol.GetTree), setup.Token);
                var (treeType, tree) = await ReadMessageAsync(stream, setup.Token);
                if (treeType != SwayIpcProtocol.GetTree)
                    throw new InvalidDataException($"unexpected sway reply type {treeType}");
                initial = ToFocus(JsonNode.Parse(tree) is { } node ? FindFocused(node) : null);
            }
            catch (OperationCanceledException ex)
            {
                throw setup.MapCancellation(ex);
            }
        }

        yield return initial;

        // Subscribe to window events; the reply is a JSON bool we can ignore.
        await stream.WriteAsync(
            SwayIpcProtocol.Encode(SwayIpcProtocol.Subscribe, "[\"window\"]"), cancellationToken);
        await ReadMessageAsync(stream, cancellationToken);

        while (true)
        {
            var (type, body) = await ReadMessageAsync(stream, cancellationToken);
            if (type != SwayIpcProtocol.WindowEvent)
                continue;

            // Decode failures drop the event, never the stream.
            FocusInfo? focus;
            try
            {
                var evt = JsonNode.Parse(body);
                if (StringValue(evt?["change"]) != "focus")
                    continue;

                var container = evt!["container"];
                focus = container is null ? null : ToFocus(container);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                Console.Error.WriteLine($"[focus-watcher] sway: dropped malformed event: {ex.Message}");
                continue;
            }

            yield return focus;
        }
    }

    private static FocusInfo? ToFocus(JsonNode? node)
    {
        if (node is not JsonObject obj)
            return null;

        return new FocusInfo(
            AppId: StringValue(obj["app_id"]),
            Title: StringValue(obj["name"]),
            Pid: IntValue(obj["pid"]));
    }

    /// <summary>Null-safe JSON extraction: wrong-typed or missing values become null, not exceptions.</summary>
    private static string? StringValue(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static int? IntValue(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<int>(out var number) ? number : null;

    private static JsonNode? FindFocused(JsonNode node)
    {
        if (node is not JsonObject obj)
            return null;
        if (obj["focused"] is JsonValue focused
            && focused.TryGetValue<bool>(out var isFocused)
            && isFocused)
            return obj;

        foreach (var key in new[] { "nodes", "floating_nodes" })
        {
            if (obj[key] is JsonArray children)
            {
                foreach (var child in children)
                {
                    if (child is not null && FindFocused(child) is { } found)
                        return found;
                }
            }
        }

        return null;
    }

    private static async Task<(uint Type, string Body)> ReadMessageAsync(
        NetworkStream stream, CancellationToken cancellationToken)
    {
        var header = new byte[SwayIpcProtocol.HeaderSize];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var (length, type) = SwayIpcProtocol.DecodeHeader(header);

        var body = new byte[length];
        await stream.ReadExactlyAsync(body, cancellationToken);
        return (type, System.Text.Encoding.UTF8.GetString(body));
    }
}