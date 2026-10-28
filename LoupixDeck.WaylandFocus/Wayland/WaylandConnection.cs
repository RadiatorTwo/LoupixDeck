using System.Buffers.Binary;
using System.Net.Sockets;

namespace LoupixDeck.WaylandFocus.Wayland;

/// <summary>
/// Minimal Wayland client connection over the compositor's Unix socket.
/// Handles the wire framing (object id, opcode, size header) and object id
/// allocation; callers deal in <see cref="WaylandMessage"/> events.
/// </summary>
public sealed class WaylandConnection : IDisposable
{
    /// <summary>The wl_display object id, fixed at 1 by the protocol.</summary>
    public const uint DisplayObjectId = 1;

    // wl_display requests.
    public const ushort DisplaySync = 0;
    public const ushort DisplayGetRegistry = 1;

    // wl_registry requests.
    public const ushort RegistryBind = 0;

    private const int HeaderSize = 8;

    private readonly Socket _socket;
    private byte[] _buffer = new byte[8192];
    private int _offset;
    private int _count;
    private uint _nextObjectId = 2;

    /// <summary>Connects to the session compositor ($XDG_RUNTIME_DIR/$WAYLAND_DISPLAY).</summary>
    public static string DefaultSocketPath()
    {
        var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")
            ?? throw new InvalidOperationException("XDG_RUNTIME_DIR is not set");
        var display = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") ?? "wayland-0";
        return Path.Combine(runtimeDir, display);
    }

    public WaylandConnection(string? socketPath = null)
    {
        _socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _socket.Connect(new UnixDomainSocketEndPoint(socketPath ?? DefaultSocketPath()));
    }

    /// <summary>Allocates the next client-side object id.</summary>
    public uint CreateObjectId() => _nextObjectId++;

    /// <summary>
    /// True when at least one full message header is already buffered or
    /// queued on the socket. Lets callers drain an event batch before
    /// acting on it, so multi-message state changes look atomic.
    /// </summary>
    public bool HasPendingMessage =>
        _count >= HeaderSize || _socket.Poll(0, SelectMode.SelectRead);

    /// <summary>Sends a request: 8-byte header (object id, opcode, size) followed by the marshalled arguments.</summary>
    public void Request(uint objectId, ushort opcode, Action<WaylandWriter>? writeArguments = null)
    {
        var writer = new WaylandWriter();
        writeArguments?.Invoke(writer);
        var payload = writer.ToArray();
        if (payload.Length > ushort.MaxValue - HeaderSize)
            throw new InvalidDataException(
                $"wayland message too large: {HeaderSize + payload.Length} bytes");
        var size = (ushort)(HeaderSize + payload.Length);

        var message = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(message, objectId);
        BinaryPrimitives.WriteUInt16LittleEndian(message.AsSpan(4), opcode);
        BinaryPrimitives.WriteUInt16LittleEndian(message.AsSpan(6), size);
        payload.CopyTo(message, HeaderSize);

        var sent = 0;
        while (sent < message.Length)
            sent += _socket.Send(message, sent, message.Length - sent, SocketFlags.None);
    }

    /// <summary>Blocks until the next complete message arrives.</summary>
    public WaylandMessage ReadMessage(CancellationToken cancellationToken)
    {
        while (true)
        {
            if (_count >= HeaderSize)
            {
                var size = BinaryPrimitives.ReadUInt16LittleEndian(_buffer.AsSpan(_offset + 6, 2));
                if (size < HeaderSize)
                    throw new InvalidDataException($"invalid message size {size} from compositor");

                if (_count >= size)
                {
                    var senderId = BinaryPrimitives.ReadUInt32LittleEndian(_buffer.AsSpan(_offset));
                    var opcode = BinaryPrimitives.ReadUInt16LittleEndian(_buffer.AsSpan(_offset + 4));
                    var payload = _buffer.AsSpan(_offset + HeaderSize, size - HeaderSize).ToArray();
                    _offset += size;
                    _count -= size;
                    return new WaylandMessage(senderId, opcode, payload);
                }
            }

            Fill(cancellationToken);
        }
    }

    private void Fill(CancellationToken cancellationToken)
    {
        // Compact the buffer so a full message can never straddle its end.
        if (_offset > 0)
        {
            Array.Copy(_buffer, _offset, _buffer, 0, _count);
            _offset = 0;
        }

        if (_count == _buffer.Length)
        {
            var grown = new byte[_buffer.Length * 2];
            _buffer.CopyTo(grown, 0);
            _buffer = grown;
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_socket.Poll(100_000, SelectMode.SelectRead)) // 100 ms
                continue;

            var read = _socket.Receive(_buffer, _count, _buffer.Length - _count, SocketFlags.None);
            if (read == 0)
                throw new EndOfStreamException("the compositor closed the connection");
            _count += read;
            return;
        }
    }

    public void Dispose() => _socket.Dispose();
}