using System.Buffers.Binary;
using System.Text;

namespace LoupixDeck.WaylandFocus.Wayland;

/// <summary>
/// Marshals arguments of a single Wayland wire message.
/// Strings and arrays are padded to 4-byte alignment as the protocol requires.
/// </summary>
public sealed class WaylandWriter
{
    private readonly MemoryStream _stream = new();

    public void WriteUInt32(uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        _stream.Write(buffer);
    }

    public void WriteInt32(int value) => WriteUInt32(unchecked((uint)value));

    /// <summary>Writes a new object id argument (same wire representation as a uint).</summary>
    public void WriteNewId(uint id) => WriteUInt32(id);

    /// <summary>Writes a UTF-8 string argument (length prefix includes the NUL terminator).</summary>
    public void WriteString(string value)
    {
        var byteCount = Encoding.UTF8.GetByteCount(value);
        WriteUInt32((uint)(byteCount + 1)); // +1 for the NUL terminator
        var bytes = Encoding.UTF8.GetBytes(value);
        _stream.Write(bytes);
        _stream.WriteByte(0);
        var lengthWithNul = byteCount + 1;
        for (var pad = (4 - lengthWithNul % 4) % 4; pad > 0; pad--)
            _stream.WriteByte(0);
    }

    /// <summary>Writes an array argument (length prefix is the raw byte count).</summary>
    public void WriteUInt32Array(IReadOnlyList<uint> values)
    {
        WriteUInt32((uint)(values.Count * 4));
        foreach (var value in values)
            WriteUInt32(value);
        for (var pad = (4 - values.Count * 4 % 4) % 4; pad > 0; pad--)
            _stream.WriteByte(0);
    }

    public byte[] ToArray() => _stream.ToArray();
}