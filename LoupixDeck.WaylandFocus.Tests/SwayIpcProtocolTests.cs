using System.Text;
using LoupixDeck.WaylandFocus.Ipc;

namespace LoupixDeck.WaylandFocus.Tests;

public class SwayIpcProtocolTests
{
    [Fact]
    public void Encode_WritesMagicLengthTypeAndPayload()
    {
        var message = SwayIpcProtocol.Encode(SwayIpcProtocol.Subscribe, "[\"window\"]");

        Assert.Equal("i3-ipc", Encoding.ASCII.GetString(message, 0, 6));

        uint length = message[6] | (uint)message[7] << 8 | (uint)message[8] << 16 | (uint)message[9] << 24;
        uint type = message[10] | (uint)message[11] << 8 | (uint)message[12] << 16 | (uint)message[13] << 24;
        Assert.Equal(10u, length); // ["window"] is 10 bytes
        Assert.Equal(SwayIpcProtocol.Subscribe, type);
        Assert.Equal("[\"window\"]", Encoding.UTF8.GetString(message, SwayIpcProtocol.HeaderSize, (int)length));
    }

    [Fact]
    public void DecodeHeader_RoundTripsEncodedMessage()
    {
        var message = SwayIpcProtocol.Encode(SwayIpcProtocol.GetTree, "{}");

        var (length, type) = SwayIpcProtocol.DecodeHeader(message);

        Assert.Equal(2u, length);
        Assert.Equal(SwayIpcProtocol.GetTree, type);
    }

    [Fact]
    public void DecodeHeader_RejectsBadMagic()
    {
        var message = SwayIpcProtocol.Encode(0);
        message[0] = (byte)'x';

        Assert.Throws<InvalidDataException>(() => SwayIpcProtocol.DecodeHeader(message));
    }

    [Fact]
    public void DecodeHeader_RejectsTruncatedHeader()
    {
        Assert.Throws<InvalidDataException>(
            () => SwayIpcProtocol.DecodeHeader(new byte[SwayIpcProtocol.HeaderSize - 1]));
    }

    [Fact]
    public void WindowEvent_HasEventFlagAndNumber()
    {
        Assert.Equal((1u << 30) | 3, SwayIpcProtocol.WindowEvent);
    }
}
