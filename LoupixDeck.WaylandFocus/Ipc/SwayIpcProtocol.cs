using System.Buffers.Binary;
using System.Text;

namespace LoupixDeck.WaylandFocus.Ipc;

/// <summary>
/// Framing of the i3/sway IPC protocol: 6-byte "i3-ipc" magic, u32 LE
/// payload length, u32 LE message type, then the (JSON) payload.
/// </summary>
public static class SwayIpcProtocol
{
    private static ReadOnlySpan<byte> Magic => "i3-ipc"u8;
    public const int HeaderSize = 14;

    /// <summary>
    /// Ceiling checked before any payload is allocated. The protocol defines no
    /// maximum — sway-ipc(7) documents only the u32 length field, and sway's own
    /// client mallocs whatever the header claims (common/ipc-client.c) — leaving
    /// roughly 4 GB to a corrupt or hostile frame. The largest reply this source
    /// asks for is <c>get_tree</c>, the whole workspace/window tree as JSON, so
    /// 32 MB is generous headroom for any real session while turning a bad length
    /// into a protocol error instead of an allocation failure.
    /// </summary>
    public const uint MaxPayloadLength = 32 * 1024 * 1024;

    public const uint Subscribe = 2;
    public const uint GetTree = 4;

    /// <summary>Window event message type: event flag (1 &lt;&lt; 30) plus event number 3.</summary>
    public const uint WindowEvent = (1u << 30) | 3;

    public static byte[] Encode(uint type, string payload = "")
    {
        byte[] body = Encoding.UTF8.GetBytes(payload);
        byte[] message = new byte[HeaderSize + body.Length];
        Magic.CopyTo(message);
        BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(6), (uint)body.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(10), type);
        body.CopyTo(message.AsSpan(HeaderSize));
        return message;
    }

    public static (uint Length, uint Type) DecodeHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length < HeaderSize)
            throw new InvalidDataException($"sway IPC header must be {HeaderSize} bytes");
        if (!header[..Magic.Length].SequenceEqual(Magic))
            throw new InvalidDataException("sway IPC header has bad magic");

        uint length = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(6, 4));
        if (length > MaxPayloadLength)
            throw new InvalidDataException(
                $"sway IPC payload length {length} exceeds the {MaxPayloadLength} byte limit");

        uint type = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(10, 4));
        return (length, type);
    }
}