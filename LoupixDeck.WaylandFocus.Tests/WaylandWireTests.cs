using LoupixDeck.WaylandFocus.Wayland;

namespace LoupixDeck.WaylandFocus.Tests;

public class WaylandWireTests
{
    [Fact]
    public void String_IsLengthPrefixedNulTerminatedAndPadded()
    {
        WaylandWriter writer = new();
        writer.WriteString("ab");

        // u32 length (3, including NUL), 'a' 'b' '\0', one pad byte to align to 4.
        Assert.Equal(new byte[] { 3, 0, 0, 0, (byte)'a', (byte)'b', 0, 0 }, writer.ToArray());
    }

    [Fact]
    public void String_RoundTrips()
    {
        WaylandWriter writer = new();
        writer.WriteString("zcosmic_toplevel_info_v1");

        WaylandReader reader = new(writer.ToArray());
        Assert.Equal("zcosmic_toplevel_info_v1", reader.ReadString());
    }

    [Fact]
    public void NullString_ReadsAsEmpty()
    {
        WaylandReader reader = new(new byte[] { 0, 0, 0, 0 });
        Assert.Equal(string.Empty, reader.ReadString());
    }

    private static readonly uint[] expected = new uint[] { 2u, 3u };

    [Fact]
    public void MixedArguments_RoundTripInOrder()
    {
        WaylandWriter writer = new();
        writer.WriteUInt32(42);
        writer.WriteString("x");
        writer.WriteUInt32Array([2u, 3u]);
        writer.WriteInt32(-5);

        WaylandReader reader = new(writer.ToArray());
        Assert.Equal(42u, reader.ReadUInt32());
        Assert.Equal("x", reader.ReadString());
        Assert.Equal(expected, reader.ReadUInt32Array());
        Assert.Equal(-5, reader.ReadInt32());
    }

    [Fact]
    public void EmptyArray_RoundTrips()
    {
        WaylandWriter writer = new();
        writer.WriteUInt32Array([]);

        Assert.Equal(new byte[] { 0, 0, 0, 0 }, writer.ToArray());
        Assert.Empty(new WaylandReader(writer.ToArray()).ReadUInt32Array());
    }

    [Fact]
    public void Utf8String_UsesUtf8ByteCount()
    {
        WaylandWriter writer = new();
        writer.WriteString("öä");

        WaylandReader reader = new(writer.ToArray());
        Assert.Equal("öä", reader.ReadString());
    }

    [Fact]
    public void TruncatedPayload_ThrowsInvalidData_NotIndexError()
    {
        // Bounds-checked decoding: a short payload is a protocol error the
        // caller can catch uniformly, not an ArgumentOutOfRangeException.
        AssertMalformedReadThrows([1, 2], ReadKind.UInt32);
    }

    [Fact]
    public void StringLengthOverrun_ThrowsInvalidData()
    {
        // Claims a 100-byte string inside an 8-byte payload.
        AssertMalformedReadThrows([100, 0, 0, 0, (byte)'a', (byte)'b', (byte)'c', 0], ReadKind.String);
    }

    [Fact]
    public void AbsurdArrayLength_ThrowsInvalidData_InsteadOfAllocating()
    {
        // A hostile length must be rejected before it reaches an allocator.
        AssertMalformedReadThrows([0xFF, 0xFF, 0xFF, 0x7F], ReadKind.UInt32Array);
    }

    [Fact]
    public void ArrayOverrun_ThrowsInvalidData()
    {
        // Claims 8 bytes of array inside a 4-byte payload.
        AssertMalformedReadThrows([8, 0, 0, 0, 1, 0, 0, 0], ReadKind.UInt32Array);
    }

    private enum ReadKind
    {
        UInt32,
        String,
        UInt32Array,
    }

    // WaylandReader is a ref struct and cannot be captured by the
    // Assert.Throws delegate, so the read runs inside this helper instead.
    private static void AssertMalformedReadThrows(byte[] payload, ReadKind kind)
    {
        WaylandReader reader = new(payload);
        try
        {
            switch (kind)
            {
                case ReadKind.UInt32:
                    reader.ReadUInt32();
                    break;
                case ReadKind.String:
                    reader.ReadString();
                    break;
                case ReadKind.UInt32Array:
                    reader.ReadUInt32Array();
                    break;
            }

            Assert.Fail($"expected InvalidDataException for {kind}");
        }
        catch (InvalidDataException)
        {
            // expected
        }
    }
}