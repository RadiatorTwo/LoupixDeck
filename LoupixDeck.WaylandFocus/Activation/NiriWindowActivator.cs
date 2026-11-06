using System.Globalization;
using System.Net.Sockets;
using System.Text.Json;

namespace LoupixDeck.WaylandFocus.Activation;

/// <summary>
/// Focuses a window on niri: the <c>"Windows"</c> request lists the windows, the
/// <c>FocusWindow</c> action focuses one. Each request is one JSON line answered by one JSON
/// line, <c>{"Ok": …}</c> or <c>{"Err": "…"}</c>.
/// </summary>
public sealed class NiriWindowActivator : IWindowActivator
{
    public string Compositor => "niri";

    public async Task<WindowActivationResult> ActivateAsync(
        Func<ToplevelWindow, bool> match, CancellationToken cancellationToken)
    {
        string socketPath = Environment.GetEnvironmentVariable("NIRI_SOCKET")
            ?? throw new InvalidOperationException("NIRI_SOCKET is not set (are we inside a niri session?)");

        using Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellationToken);
        await using NetworkStream stream = new(socket, ownsSocket: true);
        using StreamReader reader = new(stream);

        string reply = await RequestAsync(stream, reader, "\"Windows\"", cancellationToken);
        List<(ToplevelWindow, string)> windows = [];
        using (JsonDocument document = JsonDocument.Parse(reply))
        {
            JsonElement list = Ok(document.RootElement).GetProperty("Windows");

            // focus_timestamp (newer niri) orders by recency; without it niri's order stands.
            IEnumerable<JsonElement> ordered = list.EnumerateArray()
                .OrderByDescending(FocusTimestamp);
            foreach (JsonElement window in ordered)
            {
                windows.Add((new ToplevelWindow(
                    AppId: OptionalString(window, "app_id"),
                    Title: OptionalString(window, "title"),
                    Pid: window.TryGetProperty("pid", out JsonElement pid) && pid.ValueKind == JsonValueKind.Number
                        ? pid.GetInt32()
                        : null,
                    IsFocused: window.TryGetProperty("is_focused", out JsonElement focused)
                               && focused.ValueKind == JsonValueKind.True),
                    window.GetProperty("id").GetUInt64().ToString(CultureInfo.InvariantCulture)));
            }
        }

        (WindowActivationResult result, string? id) = WindowPicker.Pick(windows, match);
        if (id is null)
            return result;

        string actionReply = await RequestAsync(stream, reader,
            $"{{\"Action\":{{\"FocusWindow\":{{\"id\":{id}}}}}}}", cancellationToken);
        using (JsonDocument document = JsonDocument.Parse(actionReply))
            Ok(document.RootElement);

        return result;
    }

    private static async Task<string> RequestAsync(
        NetworkStream stream, StreamReader reader, string request, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(request + "\n"), cancellationToken);
        return await reader.ReadLineAsync(cancellationToken)
               ?? throw new EndOfStreamException("niri closed the connection without a reply");
    }

    private static JsonElement Ok(JsonElement reply)
    {
        if (reply.TryGetProperty("Ok", out JsonElement ok))
            return ok;

        string error = reply.TryGetProperty("Err", out JsonElement err) ? err.ToString() : reply.ToString();
        throw new InvalidOperationException($"niri refused the request: {SafeText.Strip(error)}");
    }

    private static string? OptionalString(JsonElement element, string name)
        => element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// Seconds of the last focus, or -1 when niri does not report it. Kept loose on purpose:
    /// the field is an optional object whose exact shape has changed between releases.
    /// </summary>
    private static double FocusTimestamp(JsonElement window)
    {
        if (!window.TryGetProperty("focus_timestamp", out JsonElement stamp))
            return -1;

        if (stamp.ValueKind == JsonValueKind.Object
            && stamp.TryGetProperty("secs", out JsonElement secs) && secs.ValueKind == JsonValueKind.Number)
        {
            double nanos = stamp.TryGetProperty("nanos", out JsonElement n) && n.ValueKind == JsonValueKind.Number
                ? n.GetDouble()
                : 0;
            return secs.GetDouble() + (nanos / 1e9);
        }

        return -1;
    }
}
