using System.Buffers.Binary;
using System.Text;

namespace LoupixDeck.WaylandFocus.Wayland;

/// <summary>
/// Reads arguments from the payload of a single Wayland wire message.
/// Every read is bounds-checked; a truncated or malformed payload raises
/// <see cref="InvalidDataException"/> instead of an indexing exception,
/// so callers can treat decode failures uniformly.
/// </summary>
public ref struct WaylandReader
{
    private readonly ReadOnlySpan<byte> _payload;
    private int _position;

    public WaylandReader(ReadOnlySpan<byte> payload)
    {
        _payload = payload;
        _position = 0;
    }

    public uint ReadUInt32()
    {
        Require(4);
        var value = BinaryPrimitives.ReadUInt32LittleEndian(_payload[_position..]);
        _position += 4;
        return value;
    }

    public int ReadInt32() => unchecked((int)ReadUInt32());

    /// <summary>Reads a UTF-8 string argument, skipping its NUL terminator and alignment padding.</summary>
    public string ReadString()
    {
        var length = ReadUInt32();
        if (length == 0)
            return string.Empty; // NULL string
        if (length > int.MaxValue)
            throw new InvalidDataException($"string length {length} exceeds the payload");

        Require((int)length);
        var value = Encoding.UTF8.GetString(_payload.Slice(_position, (int)length - 1));
        _position += (int)length;
        _position = Align4(_position);
        return value;
    }

    /// <summary>Reads an array of 32-bit values.</summary>
    public uint[] ReadUInt32Array()
    {
        var byteLength = ReadUInt32();
        if (byteLength > int.MaxValue)
            throw new InvalidDataException($"array length {byteLength} exceeds the payload");

        var paddedLength = Align4((int)byteLength);
        Require(paddedLength);

        var values = new uint[byteLength / 4];
        for (var i = 0; i < values.Length; i++)
            values[i] = ReadUInt32();
        _position += paddedLength - (int)byteLength;
        return values;
    }

    private void Require(int count)
    {
        if (_position + count > _payload.Length)
            throw new InvalidDataException(
                $"wayland message payload overrun: needed {count} bytes at offset {_position}, " +
                $"payload is {_payload.Length} bytes");
    }

    private static int Align4(int value) => (value + 3) & ~3;
}