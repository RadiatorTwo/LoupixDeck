using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using LoupixDeck.WaylandFocus.Ipc;

namespace LoupixDeck.WaylandFocus.Sources;

/// <summary>
/// Focus source for Hyprland. The event socket announces focus changes as
/// "activewindowv2>>ADDRESS" without a PID, so the window details are
/// resolved through `hyprctl -j clients` on each change.
/// </summary>
public sealed class HyprlandFocusSource : IFocusSource
{
    public string Compositor => "hyprland";

    public async IAsyncEnumerable<FocusInfo?> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var signature = Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE")
            ?? throw new InvalidOperationException(
                "HYPRLAND_INSTANCE_SIGNATURE is not set (are we inside a Hyprland session?)");
        var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")
            ?? throw new InvalidOperationException("XDG_RUNTIME_DIR is not set");

        var socketPath = Path.Combine(runtimeDir, "hypr", signature, ".socket2.sock");
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        socket.Connect(new UnixDomainSocketEndPoint(socketPath));
        using var reader = new StreamReader(new NetworkStream(socket, ownsSocket: true));

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            // Decode/resolve failures drop the event, never the stream.
            FocusInfo? focus;
            try
            {
                if (!HyprlandEvent.TryParse(line, out var evt) || evt.Name != "activewindowv2")
                    continue;

                focus = string.IsNullOrEmpty(evt.Data)
                    ? null // no window is focused
                    : await ResolveWindowAsync(evt.Data, cancellationToken);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
            {
                Console.Error.WriteLine($"[focus-watcher] hyprland: dropped malformed event: {ex.Message}");
                continue;
            }

            yield return focus;
        }
    }

    private static async Task<FocusInfo?> ResolveWindowAsync(string address, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo("hyprctl") { RedirectStandardOutput = true };
        startInfo.ArgumentList.Add("-j");
        startInfo.ArgumentList.Add("clients");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("failed to start hyprctl");
        using var output = process.StandardOutput;
        var json = await output.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        using var document = JsonDocument.Parse(json);
        foreach (var client in document.RootElement.EnumerateArray())
        {
            if (!string.Equals(client.GetProperty("address").GetString(), address,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            var pidElement = client.GetProperty("pid");
            return new FocusInfo(
                AppId: client.GetProperty("class").GetString(),
                Title: client.GetProperty("title").GetString(),
                Pid: pidElement.ValueKind == JsonValueKind.Number ? pidElement.GetInt32() : null);
        }

        return null;
    }
}