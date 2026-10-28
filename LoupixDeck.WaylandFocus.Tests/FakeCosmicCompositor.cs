using System.Buffers.Binary;
using System.Net.Sockets;
using LoupixDeck.WaylandFocus.Wayland;

namespace LoupixDeck.WaylandFocus.Tests;

/// <summary>
/// A scripted stand-in for cosmic-comp. Speaks just enough of the Wayland
/// wire protocol to drive <see cref="Sources.CosmicFocusSource"/> through a
/// complete story: registry globals, the zcosmic_toplevel_info_v1 bind,
/// two initial toplevels, a focus switch and a close. Also supports a
/// stall mode to exercise the client's setup timeout.
/// </summary>
internal sealed class FakeCosmicCompositor : IDisposable
{
    // Server-assigned object ids must live in the server range.
    internal const uint HandleOne = 0xFF000001;
    internal const uint HandleTwo = 0xFF000002;

    // Event opcodes used in the script.
    private const ushort InfoToplevel = 0;
    private const ushort HandleClosed = 0;
    private const ushort HandleDone = 1;
    private const ushort HandleTitle = 2;
    private const ushort HandleAppId = 3;
    private const ushort HandleState = 8;
    private const uint StateActivated = 2;

    private readonly Socket _listener;
    private readonly string _directory;
    private readonly object _sendLock = new();
    private Socket? _client;
    private uint _managerId;

    public string SocketPath { get; }

    /// <summary>When set, the initial toplevel batch is never sent — the client should time out.</summary>
    public bool StallBeforeInitialBatch { get; init; }

    public FakeCosmicCompositor()
    {
        // CreateTempSubdirectory yields a 0700 directory: other local users
        // must not be able to reach a listening socket.
        _directory = Directory.CreateTempSubdirectory("focus-watcher-test-").FullName;
        SocketPath = Path.Combine(_directory, "wayland-test");

        _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _listener.Bind(new UnixDomainSocketEndPoint(SocketPath));
        _listener.Listen(1);
        _ = AcceptAndServeAsync();
    }

    /// <summary>Moves focus from handle one to handle two, the way cosmic-comp would.</summary>
    public void SwitchFocus()
    {
        // One write, like a real compositor flush: the whole batch hits the
        // client socket atomically for these sizes.
        Send(
            Event(HandleOne, HandleState, w => w.WriteUInt32Array([])),
            Event(HandleOne, HandleDone, _ => { }),
            Event(HandleTwo, HandleState, w => w.WriteUInt32Array([StateActivated])),
            Event(HandleTwo, HandleDone, _ => { }));
    }

    /// <summary>Activates both handles in one batch; the later activation must win.</summary>
    public void ActivateBothHandles() => Send(
        Event(HandleOne, HandleState, w => w.WriteUInt32Array([StateActivated])),
        Event(HandleOne, HandleDone, _ => { }),
        Event(HandleTwo, HandleState, w => w.WriteUInt32Array([StateActivated])),
        Event(HandleTwo, HandleDone, _ => { }));

    /// <summary>Closes the currently focused toplevel (handle two).</summary>
    public void CloseFocusedHandle() => Send(Event(HandleTwo, HandleClosed, _ => { }));

    /// <summary>Re-sends the unchanged activated state of a handle; must not produce a focus event.</summary>
    public void SendRedundantState(uint handle)
    {
        Send(Event(handle, HandleState, w => w.WriteUInt32Array([StateActivated])));
        Send(Event(handle, HandleDone, _ => { }));
    }

    private async Task AcceptAndServeAsync()
    {
        var client = await _listener.AcceptAsync();
        _client = client;
        try
        {
            await ServeAsync(client);
        }
        catch (Exception)
        {
            // Connection torn down at test end.
        }
    }

    private async Task ServeAsync(Socket client)
    {
        uint registryId = 0;
        var syncCount = 0;

        while (true)
        {
            var (sender, opcode, payload) = await ReadMessageAsync(client);

            if (sender == 1 /* wl_display */)
            {
                if (opcode == 1) // get_registry
                {
                    registryId = BinaryPrimitives.ReadUInt32LittleEndian(payload);
                }
                else if (opcode == 0) // sync
                {
                    var callbackId = BinaryPrimitives.ReadUInt32LittleEndian(payload);
                    syncCount++;

                    if (syncCount == 1)
                    {
                        // wl_registry.global: advertise the protocol.
                        Send(Event(registryId, 0, w =>
                        {
                            w.WriteUInt32(1);
                            w.WriteString("zcosmic_toplevel_info_v1");
                            w.WriteUInt32(3);
                        }));
                    }

                    if (syncCount == 2)
                    {
                        if (StallBeforeInitialBatch)
                            continue; // never reply: the client must time out

                        // Initial toplevel batch, sent right after the bind —
                        // before the sync reply, as a real compositor does.
                        SendToplevel(HandleOne, "app.one", "First", activated: true);
                        SendToplevel(HandleTwo, "app.two", "Second", activated: false);
                    }

                    Send(Event(callbackId, 0, w => w.WriteUInt32((uint)syncCount))); // done
                }
            }
            else if (sender == registryId && opcode == 0) // bind
            {
                // Payload: u32 name, string interface, u32 version, u32 new_id.
                _managerId = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(payload.Length - 4));
            }
        }
    }

    private void SendToplevel(uint handle, string appId, string title, bool activated)
    {
        Send(
            Event(_managerId, InfoToplevel, w => w.WriteNewId(handle)),
            Event(handle, HandleAppId, w => w.WriteString(appId)),
            Event(handle, HandleTitle, w => w.WriteString(title)),
            Event(handle, HandleState, w => w.WriteUInt32Array(activated ? [StateActivated] : [])),
            Event(handle, HandleDone, _ => { }));
    }

    private void Send(params byte[][] messages)
    {
        lock (_sendLock)
        {
            var client = _client ?? throw new InvalidOperationException("client not connected yet");
            foreach (var message in messages)
            {
                var sent = 0;
                while (sent < message.Length)
                    sent += client.Send(message, sent, message.Length - sent, SocketFlags.None);
            }
        }
    }

    /// <summary>Encodes a server-to-client event in the same wire format.</summary>
    private static byte[] Event(uint sender, ushort opcode, Action<WaylandWriter> writeArguments)
    {
        var writer = new WaylandWriter();
        writeArguments(writer);
        var payload = writer.ToArray();

        var message = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(message, sender);
        BinaryPrimitives.WriteUInt16LittleEndian(message.AsSpan(4), opcode);
        BinaryPrimitives.WriteUInt16LittleEndian(message.AsSpan(6), (ushort)message.Length);
        payload.CopyTo(message.AsSpan(8));
        return message;
    }

    private static async Task<(uint Sender, ushort Opcode, byte[] Payload)> ReadMessageAsync(
        Socket client)
    {
        var header = new byte[8];
        await ReadExactlyAsync(client, header);
        var size = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(6, 2));

        var payload = new byte[size - 8];
        await ReadExactlyAsync(client, payload);

        return (
            BinaryPrimitives.ReadUInt32LittleEndian(header),
            BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(4, 2)),
            payload);
    }

    private static async Task ReadExactlyAsync(Socket client, Memory<byte> buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await client.ReceiveAsync(buffer[read..], SocketFlags.None);
            if (count == 0)
                throw new EndOfStreamException();
            read += count;
        }
    }

    public void Dispose()
    {
        _client?.Dispose();
        _listener.Dispose();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
