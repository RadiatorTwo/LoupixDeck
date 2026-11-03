using System.Runtime.CompilerServices;
using System.Text;
using LoupixDeck.WaylandFocus.Wayland;

namespace LoupixDeck.WaylandFocus.Sources;

/// <summary>
/// Focus source for the COSMIC compositor (cosmic-comp).
/// <para>
/// COSMIC exposes no D-Bus API for focus, so this talks the
/// zcosmic_toplevel_info_v1 Wayland protocol directly (the same protocol the
/// COSMIC dock uses). We bind version 1, which lists every toplevel via the
/// <c>toplevel</c> event and reports properties per handle: <c>title</c>,
/// <c>app_id</c>, and a <c>state</c> array where the value 2 means
/// "activated" (focused).
/// </para>
/// <para>
/// The protocol does not expose PIDs; the owning process is resolved from
/// app_id by scanning /proc (see <see cref="LoupixDeck.WaylandFocus.ProcessNamer"/>).
/// </para>
/// </summary>
public sealed class CosmicFocusSource(
    string? socketPath = null,
    TextWriter? trace = null,
    TimeSpan? setupTimeout = null) : IFocusSource
{
    private const string InterfaceName = "zcosmic_toplevel_info_v1";

    // zcosmic_toplevel_info_v1 events.
    private const ushort InfoToplevel = 0;

    // zcosmic_toplevel_handle_v1 events (only the ones we act on).
    private const ushort HandleClosed = 0;
    private const ushort HandleDone = 1;
    private const ushort HandleTitle = 2;
    private const ushort HandleAppId = 3;
    private const ushort HandleState = 8;

    // Entry value of the zcosmic_toplevel_handle_v1.state enum.
    private const uint StateActivated = 2;

    // Why sanitized here and not per call site: every trace line can carry
    // application-controlled text, and the trace ends on the operator's
    // terminal — one choke point cannot be forgotten.
    private readonly TextWriter? _trace =
        trace is null ? null : new SanitizingTextWriter(trace);

    // Strictly increasing per activation, so a (buggy) batch that activates
    // two handles still resolves to the most recent one, never dictionary order.
    private int _activationCounter;

    public string Compositor => "cosmic";

    public async IAsyncEnumerable<FocusInfo?> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // The handshake below blocks on socket reads; yield first so callers
        // get a pending task instead of a pinned thread.
        await Task.Yield();

        using WaylandConnection connection = new(socketPath);

        uint manager;
        Dictionary<uint, ToplevelState> handles;
        FocusInfo? initial;

        using (SetupPhase setup = new(
                   $"the {InterfaceName} handshake", setupTimeout, cancellationToken))
        {
            try
            {
                (manager, handles) = Handshake(connection, setup.Token);
                initial = CurrentFocus(handles);
            }
            catch (OperationCanceledException ex)
            {
                throw setup.MapCancellation(ex);
            }
        }

        // The first event is always the state we started in — null (no
        // focused toplevel) included — so consumers can adopt the current
        // profile without waiting for the first switch.
        yield return initial;

        FocusInfo? last = initial;
        while (true)
        {
            // Yield so the socket wait does not run on the consumer's thread. The wait
            // itself blocks — Fill polls every 100 ms (also how cancellation is observed),
            // parking this thread-pool thread until the next event; one parked thread in a
            // quiet session is the accepted cost of the hand-rolled client. ReadMessage
            // documents that it blocks.
            await Task.Yield();

            WaylandMessage message = connection.ReadMessage(cancellationToken);
            ProcessMessage(manager, handles, message);

            // A focus switch arrives as several messages (deactivate, done,
            // activate, done). Drain whatever is already queued so the
            // half-switched intermediate state is never reported.
            while (connection.HasPendingMessage)
            {
                message = connection.ReadMessage(cancellationToken);
                ProcessMessage(manager, handles, message);
            }

            FocusInfo? current = CurrentFocus(handles);
            if (current == last)
                continue;

            last = current;
            _trace?.WriteLine(current is null
                ? "[focus] -> (none)"
                : $"[focus] -> \"{current.Title}\" (app_id={current.AppId})");
            yield return current;
        }
    }

    /// <summary>Connects, binds the protocol and ingests the initial toplevel batch.</summary>
    private (uint Manager, Dictionary<uint, ToplevelState> Handles) Handshake(
        WaylandConnection connection, CancellationToken token)
    {
        uint registry = connection.CreateObjectId();
        connection.Request(WaylandConnection.DisplayObjectId, WaylandConnection.DisplayGetRegistry,
            w => w.WriteNewId(registry));

        // Round trip 1: collect registry globals until the sync callback completes.
        uint sync = connection.CreateObjectId();
        connection.Request(WaylandConnection.DisplayObjectId, WaylandConnection.DisplaySync,
            w => w.WriteNewId(sync));

        uint? infoGlobalName = null;
        uint infoGlobalVersion = 0u;
        while (true)
        {
            WaylandMessage message = connection.ReadMessage(token);
            if (message.SenderId == registry && message.Opcode == 0 /* global */)
            {
                WaylandReader reader = new(message.Payload);
                uint name = reader.ReadUInt32();
                string iface = reader.ReadString();
                uint version = reader.ReadUInt32();
                if (iface == InterfaceName)
                {
                    infoGlobalName = name;
                    infoGlobalVersion = version;
                }
            }
            else if (message.SenderId == sync && message.Opcode == 0 /* done */)
            {
                break;
            }
        }

        if (infoGlobalName is null)
        {
            throw new NotSupportedException(
                $"the compositor does not advertise the {InterfaceName} protocol; " +
                "this source only works on COSMIC (cosmic-comp)");
        }

        // Bind version 1: the only version that sends standalone toplevel events.
        uint manager = connection.CreateObjectId();
        uint bindVersion = Math.Min(infoGlobalVersion, 1u);
        connection.Request(registry, WaylandConnection.RegistryBind, w =>
        {
            w.WriteUInt32(infoGlobalName.Value);
            w.WriteString(InterfaceName);
            w.WriteUInt32(bindVersion);
            w.WriteNewId(manager);
        });

        // Round trip 2: all existing toplevels and their initial properties are
        // sent right after the bind, so the sync callback means "initial state complete".
        sync = connection.CreateObjectId();
        connection.Request(WaylandConnection.DisplayObjectId, WaylandConnection.DisplaySync,
            w => w.WriteNewId(sync));

        Dictionary<uint, ToplevelState> handles = new();
        while (true)
        {
            WaylandMessage message = connection.ReadMessage(token);
            if (message.SenderId == sync && message.Opcode == 0 /* done */)
                break;
            ProcessMessage(manager, handles, message);
        }

        return (manager, handles);
    }

    /// <summary>
    /// Applies one message to the handle table. A malformed message is
    /// dropped with a note rather than faulting the stream: message
    /// decoders must not be able to kill the watcher.
    /// </summary>
    private void ProcessMessage(
        uint manager,
        Dictionary<uint, ToplevelState> handles,
        WaylandMessage message)
    {
        try
        {
            if (message.SenderId == manager)
            {
                if (message.Opcode == InfoToplevel)
                {
                    WaylandReader reader = new(message.Payload);
                    uint handleId = reader.ReadUInt32(); // id assigned by the compositor
                    handles[handleId] = new ToplevelState();
                    _trace?.WriteLine($"[wayland] new toplevel handle 0x{handleId:X}");
                }

                return; // finished/done: nothing to track for a v1 client
            }

            if (!handles.TryGetValue(message.SenderId, out ToplevelState? state))
            {
                _trace?.WriteLine($"[wayland] event sender=0x{message.SenderId:X} opcode={message.Opcode} (ignored)");
                return;
            }

            WaylandReader args = new(message.Payload);
            switch (message.Opcode)
            {
                case HandleClosed:
                    handles.Remove(message.SenderId);
                    _trace?.WriteLine($"[wayland] handle 0x{message.SenderId:X} closed");
                    break;
                case HandleTitle:
                    state.Title = args.ReadString();
                    _trace?.WriteLine($"[wayland] handle 0x{message.SenderId:X} title -> \"{state.Title}\"");
                    break;
                case HandleAppId:
                    state.AppId = args.ReadString();
                    _trace?.WriteLine($"[wayland] handle 0x{message.SenderId:X} app_id -> \"{state.AppId}\"");
                    break;
                case HandleState:
                    state.Activated = Array.IndexOf(args.ReadUInt32Array(), StateActivated) >= 0;
                    if (state.Activated)
                        state.ActivationOrder = ++_activationCounter;
                    _trace?.WriteLine($"[wayland] handle 0x{message.SenderId:X} state -> activated={state.Activated}");
                    break;
                case HandleDone:
                    break; // state is applied eagerly; done just delimits atomic batches
                default:
                    _trace?.WriteLine($"[wayland] handle 0x{message.SenderId:X} event opcode={message.Opcode} (ignored)");
                    break;
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            Console.WriteLine(
                $"[AppSwitch] cosmic: dropped malformed message " +
                $"sender=0x{message.SenderId:X} opcode={message.Opcode}: {ex.Message}");
        }
    }

    /// <summary>The focused window's identity, or null when no toplevel holds focus.</summary>
    private static FocusInfo? CurrentFocus(Dictionary<uint, ToplevelState> handles)
    {
        ToplevelState? activated = null;
        foreach (ToplevelState state in handles.Values)
        {
            if (state.Activated && (activated is null || state.ActivationOrder > activated.ActivationOrder))
                activated = state;
        }

        return activated is null
            ? null
            : new FocusInfo(activated.AppId, activated.Title, Pid: null);
    }

    private sealed class ToplevelState
    {
        public string? AppId { get; set; }
        public string? Title { get; set; }
        public bool Activated { get; set; }
        public int ActivationOrder { get; set; }
    }

    /// <summary>Passes every trace line through <see cref="SafeText.Strip"/>.</summary>
    private sealed class SanitizingTextWriter(TextWriter inner) : TextWriter
    {
        public override Encoding Encoding => inner.Encoding;

        public override void WriteLine(string? value)
            => inner.WriteLine(SafeText.Strip(value));
    }
}