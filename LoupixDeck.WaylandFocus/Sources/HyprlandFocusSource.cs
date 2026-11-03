using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using LoupixDeck.WaylandFocus.Ipc;

namespace LoupixDeck.WaylandFocus.Sources;

/// <summary>
/// Focus source for Hyprland. The event socket announces focus changes as
/// "activewindowv2>>ADDRESS" without a PID, so the window details are resolved
/// by querying `j/clients` over Hyprland's command socket - the transport
/// hyprctl itself uses. The enumeration ends when the event socket closes;
/// the runner reconnects with a backoff.
/// </summary>
/// <remarks>Test seam: explicit socket paths; null falls back to the session's.</remarks>
public sealed class HyprlandFocusSource(string? eventSocketPath = null, string? commandSocketPath = null) : IFocusSource
{
    private readonly string? _eventSocketPath = eventSocketPath;
    private readonly string? _commandSocketPath = commandSocketPath;

    public string Compositor => "hyprland";

    public async IAsyncEnumerable<FocusInfo?> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string socketPath = _eventSocketPath
            ?? Path.Combine(RuntimeDirectory(), InstanceSignature(), ".socket2.sock");
        string commandSocketPath = _commandSocketPath
            ?? Path.Combine(RuntimeDirectory(), InstanceSignature(), ".socket.sock");

        using Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        socket.Connect(new UnixDomainSocketEndPoint(socketPath));
        using StreamReader reader = new(new NetworkStream(socket, ownsSocket: true));

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            // Decode/resolve failures drop the event, never the stream.
            FocusInfo? focus;
            try
            {
                if (!HyprlandEvent.TryParse(line, out HyprlandEvent evt) || evt.Name != "activewindowv2")
                    continue;

                focus = string.IsNullOrEmpty(evt.Data)
                    ? null // no window is focused
                    : await ResolveWindowAsync(commandSocketPath, evt.Data, cancellationToken);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException
                or KeyNotFoundException or SocketException or IOException)
            {
                Console.WriteLine($"[AppSwitch] hyprland: dropped malformed event: {ex.Message}");
                continue;
            }

            yield return focus;
        }
    }

    private static string InstanceSignature() =>
        Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE")
            ?? throw new InvalidOperationException(
                "HYPRLAND_INSTANCE_SIGNATURE is not set (are we inside a Hyprland session?)");

    private static string RuntimeDirectory() =>
        Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")
            ?? throw new InvalidOperationException("XDG_RUNTIME_DIR is not set");

    private static async Task<FocusInfo?> ResolveWindowAsync(
        string commandSocketPath, string address, CancellationToken cancellationToken)
    {
        string json = await QueryClientsAsync(commandSocketPath, cancellationToken);

        using JsonDocument document = JsonDocument.Parse(json);
        foreach (JsonElement client in document.RootElement.EnumerateArray())
        {
            if (!string.Equals(StripHexPrefix(client.GetProperty("address").GetString()),
                    StripHexPrefix(address), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            JsonElement pidElement = client.GetProperty("pid");
            return new FocusInfo(
                AppId: client.GetProperty("class").GetString(),
                Title: client.GetProperty("title").GetString(),
                Pid: pidElement.ValueKind == JsonValueKind.Number ? pidElement.GetInt32() : null);
        }

        return null;
    }

    /// <summary>
    /// The event socket sends the window address as bare hex ("55c4f8c5d8f0"), while
    /// <c>j/clients</c> prefixes it with "0x" - compare them without the prefix.
    /// </summary>
    private static string? StripHexPrefix(string? address) =>
        address is not null && address.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? address[2..]
            : address;

    /// <summary>
    /// One request over Hyprland's command socket: write <c>j/clients</c>, read the
    /// reply until the server closes - byte-for-byte what hyprctl does. Hyprland
    /// answers synchronously and bounds an open connection with its
    /// own five-second timeout, so a wedged compositor reaches us as a closed
    /// connection (an empty reply becomes JsonException at the caller) rather than a
    /// hang; the connection is closed on every path because leaving one open would
    /// freeze Hyprland.
    /// </summary>
    private static async Task<string> QueryClientsAsync(
        string socketPath, CancellationToken cancellationToken)
    {
        using Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        socket.Connect(new UnixDomainSocketEndPoint(socketPath));

        using NetworkStream stream = new(socket, ownsSocket: true);
        await stream.WriteAsync(Encoding.UTF8.GetBytes("j/clients"), cancellationToken);

        using StreamReader reader = new(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}