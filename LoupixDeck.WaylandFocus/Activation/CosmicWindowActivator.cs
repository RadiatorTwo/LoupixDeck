using System.Globalization;
using LoupixDeck.WaylandFocus.Sources;
using LoupixDeck.WaylandFocus.Wayland;

namespace LoupixDeck.WaylandFocus.Activation;

/// <summary>
/// Focuses a window on COSMIC. <c>zcosmic_toplevel_info_v1</c> lists the toplevels (the
/// protocol <see cref="CosmicFocusSource"/> watches focus with), and
/// <c>zcosmic_toplevel_manager_v1.activate</c> focuses one on behalf of a <c>wl_seat</c> — the
/// same pair of protocols the COSMIC dock uses. Handles are only valid on the connection that
/// received them, so listing and activating share one connection.
/// </summary>
public sealed class CosmicWindowActivator(string? socketPath = null, TimeSpan? timeout = null) : IWindowActivator
{
    private const string InfoInterface = "zcosmic_toplevel_info_v1";
    private const string ManagerInterface = "zcosmic_toplevel_manager_v1";
    private const string SeatInterface = "wl_seat";

    // zcosmic_toplevel_info_v1 events.
    private const ushort InfoToplevel = 0;

    // zcosmic_toplevel_handle_v1 events (version 1).
    private const ushort HandleClosed = 0;
    private const ushort HandleTitle = 2;
    private const ushort HandleAppId = 3;
    private const ushort HandleState = 8;

    // zcosmic_toplevel_handle_v1.state entries.
    private const uint StateMinimized = 1;
    private const uint StateActivated = 2;

    // zcosmic_toplevel_manager_v1 requests.
    private const ushort ManagerActivate = 2;
    private const ushort ManagerUnsetMinimized = 6;

    public string Compositor => "cosmic";

    public async Task<WindowActivationResult> ActivateAsync(
        Func<ToplevelWindow, bool> match, CancellationToken cancellationToken)
    {
        // The client blocks on socket reads; keep that off the caller's thread.
        await Task.Yield();

        using SetupPhase phase = new("the COSMIC toplevel handshake", timeout, cancellationToken);
        try
        {
            return Activate(match, phase.Token);
        }
        catch (OperationCanceledException ex)
        {
            throw phase.MapCancellation(ex);
        }
    }

    private WindowActivationResult Activate(Func<ToplevelWindow, bool> match, CancellationToken token)
    {
        using WaylandConnection connection = new(socketPath);

        uint registry = connection.CreateObjectId();
        connection.Request(WaylandConnection.DisplayObjectId, WaylandConnection.DisplayGetRegistry,
            w => w.WriteNewId(registry));

        Dictionary<string, uint> globals = new(StringComparer.Ordinal);
        RoundTrip(connection, token, message =>
        {
            if (message.SenderId != registry || message.Opcode != 0 /* global */)
                return;

            WaylandReader reader = new(message.Payload);
            uint name = reader.ReadUInt32();
            string iface = reader.ReadString();
            if (iface is InfoInterface or ManagerInterface or SeatInterface)
                globals.TryAdd(iface, name);
        });

        foreach (string required in new[] { InfoInterface, ManagerInterface, SeatInterface })
        {
            if (!globals.ContainsKey(required))
                throw new NotSupportedException($"the compositor does not advertise {required}");
        }

        uint info = Bind(connection, registry, globals[InfoInterface], InfoInterface);
        uint manager = Bind(connection, registry, globals[ManagerInterface], ManagerInterface);
        uint seat = Bind(connection, registry, globals[SeatInterface], SeatInterface);

        // Every existing toplevel and its properties arrive right after the bind, so the sync
        // callback marks the list as complete.
        Dictionary<uint, Toplevel> toplevels = new();
        RoundTrip(connection, token, message => Track(info, toplevels, message));

        (WindowActivationResult result, string? handle) = WindowPicker.Pick(
            toplevels.Select(pair => (pair.Value.ToWindow(),
                pair.Key.ToString(CultureInfo.InvariantCulture))),
            match);
        if (handle is null)
            return result;

        uint target = uint.Parse(handle, CultureInfo.InvariantCulture);
        if (toplevels[target].Minimized)
            connection.Request(manager, ManagerUnsetMinimized, w => w.WriteUInt32(target));

        connection.Request(manager, ManagerActivate, w =>
        {
            w.WriteUInt32(target);
            w.WriteUInt32(seat);
        });

        // Wait until the compositor processed the requests before the connection closes.
        RoundTrip(connection, token, _ => { });
        return result;
    }

    private static uint Bind(WaylandConnection connection, uint registry, uint name, string iface)
    {
        uint id = connection.CreateObjectId();
        connection.Request(registry, WaylandConnection.RegistryBind, w =>
        {
            w.WriteUInt32(name);
            w.WriteString(iface);
            w.WriteUInt32(1); // version 1 of all three is all this needs
            w.WriteNewId(id);
        });
        return id;
    }

    /// <summary>Sends wl_display.sync and feeds every event up to its callback to <paramref name="handle"/>.</summary>
    private static void RoundTrip(WaylandConnection connection, CancellationToken token, Action<WaylandMessage> handle)
    {
        uint callback = connection.CreateObjectId();
        connection.Request(WaylandConnection.DisplayObjectId, WaylandConnection.DisplaySync,
            w => w.WriteNewId(callback));

        while (true)
        {
            WaylandMessage message = connection.ReadMessage(token);
            if (message.SenderId == callback && message.Opcode == 0 /* done */)
                return;
            if (message.SenderId == WaylandConnection.DisplayObjectId && message.Opcode == 0 /* error */)
                throw new InvalidOperationException("the compositor reported a protocol error");

            handle(message);
        }
    }

    private static void Track(uint info, Dictionary<uint, Toplevel> toplevels, WaylandMessage message)
    {
        try
        {
            if (message.SenderId == info)
            {
                if (message.Opcode == InfoToplevel)
                    toplevels[new WaylandReader(message.Payload).ReadUInt32()] = new Toplevel();
                return;
            }

            if (!toplevels.TryGetValue(message.SenderId, out Toplevel? toplevel))
                return;

            WaylandReader reader = new(message.Payload);
            switch (message.Opcode)
            {
                case HandleClosed:
                    toplevels.Remove(message.SenderId);
                    break;
                case HandleTitle:
                    toplevel.Title = reader.ReadString();
                    break;
                case HandleAppId:
                    toplevel.AppId = reader.ReadString();
                    break;
                case HandleState:
                    uint[] states = reader.ReadUInt32Array();
                    toplevel.Activated = Array.IndexOf(states, StateActivated) >= 0;
                    toplevel.Minimized = Array.IndexOf(states, StateMinimized) >= 0;
                    break;
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            // A malformed event loses that one property, never the whole activation.
            Console.WriteLine($"[AppActivation] cosmic: dropped malformed message opcode={message.Opcode}: {ex.Message}");
        }
    }

    private sealed class Toplevel
    {
        public string? AppId { get; set; }
        public string? Title { get; set; }
        public bool Activated { get; set; }
        public bool Minimized { get; set; }

        // COSMIC withholds PIDs; the caller resolves the process from the app_id.
        public ToplevelWindow ToWindow() => new(AppId, Title, Pid: null, IsFocused: Activated);
    }
}
