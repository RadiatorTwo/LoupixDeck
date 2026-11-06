using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using LoupixDeck.WaylandFocus.Ipc;

namespace LoupixDeck.WaylandFocus.Activation;

/// <summary>
/// Focuses a window on sway (and i3-IPC compatible compositors): <c>GET_TREE</c> lists the
/// windows, <c>RUN_COMMAND [con_id=N] focus</c> focuses one.
/// </summary>
public sealed class SwayWindowActivator : IWindowActivator
{
    public string Compositor => "sway";

    public async Task<WindowActivationResult> ActivateAsync(
        Func<ToplevelWindow, bool> match, CancellationToken cancellationToken)
    {
        using Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(SocketPath()), cancellationToken);
        await using NetworkStream stream = new(socket, ownsSocket: true);

        string tree = await RequestAsync(stream, SwayIpcProtocol.GetTree, string.Empty, cancellationToken);
        List<(ToplevelWindow, string)> windows = [];
        if (JsonNode.Parse(tree) is { } root)
            CollectWindows(root, windows);

        (WindowActivationResult result, string? conId) = WindowPicker.Pick(windows, match);
        if (conId is null)
            return result;

        string reply = await RequestAsync(stream, SwayIpcProtocol.RunCommand, $"[con_id={conId}] focus", cancellationToken);
        // The reply is one {"success": bool} object per command.
        if (JsonNode.Parse(reply) is not JsonArray { Count: > 0 } outcomes
            || outcomes[0]?["success"] is not JsonValue success
            || !success.TryGetValue(out bool succeeded) || !succeeded)
        {
            Console.WriteLine($"[AppActivation] sway: focus answered {SafeText.Strip(reply)}");
        }

        return result;
    }

    private static string SocketPath()
    {
        string runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")
            ?? throw new InvalidOperationException("XDG_RUNTIME_DIR is not set");
        return Environment.GetEnvironmentVariable("SWAYSOCK")
            ?? Directory.GetFiles(runtimeDir, "sway-ipc.*.sock")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
            ?? throw new InvalidOperationException("no sway IPC socket found");
    }

    /// <summary>
    /// Walks the tree for window containers: a node with a pid and either an app_id (Wayland) or
    /// window properties (XWayland). Workspaces and outputs have neither.
    /// </summary>
    private static void CollectWindows(JsonNode node, List<(ToplevelWindow, string)> windows)
    {
        if (node is not JsonObject obj)
            return;

        int? pid = obj["pid"] is JsonValue p && p.TryGetValue(out int value) ? value : null;
        string? appId = Text(obj["app_id"]) ?? Text(obj["window_properties"]?["class"]);
        if (pid is not null && appId is not null && obj["id"] is JsonValue id && id.TryGetValue(out long conId))
        {
            bool focused = obj["focused"] is JsonValue f && f.TryGetValue(out bool isFocused) && isFocused;
            windows.Add((new ToplevelWindow(appId, Text(obj["name"]), pid, focused),
                conId.ToString(CultureInfo.InvariantCulture)));
        }

        foreach (string key in new[] { "nodes", "floating_nodes" })
        {
            if (obj[key] is JsonArray children)
            {
                foreach (JsonNode? child in children)
                {
                    if (child is not null)
                        CollectWindows(child, windows);
                }
            }
        }
    }

    private static string? Text(JsonNode? node)
        => node is JsonValue value && value.TryGetValue(out string? text) && !string.IsNullOrEmpty(text) ? text : null;

    private static async Task<string> RequestAsync(
        NetworkStream stream, uint type, string payload, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(SwayIpcProtocol.Encode(type, payload), cancellationToken);

        byte[] header = new byte[SwayIpcProtocol.HeaderSize];
        await stream.ReadExactlyAsync(header, cancellationToken);
        (uint length, _) = SwayIpcProtocol.DecodeHeader(header);

        byte[] body = new byte[length];
        await stream.ReadExactlyAsync(body, cancellationToken);
        return Encoding.UTF8.GetString(body);
    }
}
