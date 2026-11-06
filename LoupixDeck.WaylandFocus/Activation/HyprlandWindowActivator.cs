using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace LoupixDeck.WaylandFocus.Activation;

/// <summary>
/// Focuses a window on Hyprland: <c>j/clients</c> lists the windows, <c>dispatch focuswindow
/// address:…</c> focuses one — both over the command socket hyprctl uses. Hyprland answers each
/// request on its own connection and closes it, so every request opens a fresh one.
/// </summary>
public sealed class HyprlandWindowActivator(string? commandSocketPath = null) : IWindowActivator
{
    public string Compositor => "hyprland";

    public async Task<WindowActivationResult> ActivateAsync(
        Func<ToplevelWindow, bool> match, CancellationToken cancellationToken)
    {
        string socketPath = commandSocketPath ?? DefaultSocketPath();

        string json = await RequestAsync(socketPath, "j/clients", cancellationToken);
        List<(ToplevelWindow, string)> windows = [];
        using (JsonDocument document = JsonDocument.Parse(json))
        {
            // focusHistoryID orders windows by recency, 0 being the focused one; sorting by it
            // makes the most recently used match the one that is raised.
            foreach (JsonElement client in document.RootElement.EnumerateArray()
                         .OrderBy(c => c.TryGetProperty("focusHistoryID", out JsonElement h)
                                       && h.ValueKind == JsonValueKind.Number ? h.GetInt32() : int.MaxValue))
            {
                if (client.TryGetProperty("mapped", out JsonElement mapped) && mapped.ValueKind == JsonValueKind.False)
                    continue;

                string? address = client.GetProperty("address").GetString();
                if (string.IsNullOrEmpty(address))
                    continue;

                bool focused = client.TryGetProperty("focusHistoryID", out JsonElement history)
                               && history.ValueKind == JsonValueKind.Number && history.GetInt32() == 0;
                windows.Add((new ToplevelWindow(
                    AppId: client.GetProperty("class").GetString(),
                    Title: client.GetProperty("title").GetString(),
                    Pid: client.TryGetProperty("pid", out JsonElement pid) && pid.ValueKind == JsonValueKind.Number
                        ? pid.GetInt32()
                        : null,
                    IsFocused: focused), address));
            }
        }

        (WindowActivationResult result, string? target) = WindowPicker.Pick(windows, match);
        if (target is null)
            return result;

        string reply = await RequestAsync(socketPath, $"dispatch focuswindow address:{target}", cancellationToken);
        if (!reply.Trim().Equals("ok", StringComparison.OrdinalIgnoreCase))
            Console.WriteLine($"[AppActivation] hyprland: focuswindow answered '{SafeText.Strip(reply.Trim())}'");

        return result;
    }

    private static string DefaultSocketPath()
    {
        string runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")
            ?? throw new InvalidOperationException("XDG_RUNTIME_DIR is not set");
        string signature = Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE")
            ?? throw new InvalidOperationException("HYPRLAND_INSTANCE_SIGNATURE is not set");
        return Path.Combine(runtimeDir, "hypr", signature, ".socket.sock");
    }

    /// <summary>One request, one reply read until Hyprland closes the connection.</summary>
    private static async Task<string> RequestAsync(string socketPath, string request, CancellationToken cancellationToken)
    {
        using Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellationToken);

        await using NetworkStream stream = new(socket, ownsSocket: true);
        await stream.WriteAsync(Encoding.UTF8.GetBytes(request), cancellationToken);

        using StreamReader reader = new(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
