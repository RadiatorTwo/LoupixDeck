using System.Text;
using System.Threading.Channels;
using LoupixDeck.LoupedeckDevice.Virtual;

namespace LoupixDeck.LoupedeckDevice.Serial;

/// <summary>
/// Stands in for <see cref="SerialConnection"/> when the device is virtual (see
/// <see cref="Utils.VirtualDevice"/>). Every packet the device would put on the wire still arrives
/// here fully encoded; instead of being written anywhere it is acknowledged the way the firmware
/// does, and the parts that change what the hardware shows — framebuffer writes, refreshes, LED
/// colours, brightness, vibration — are mirrored into <see cref="VirtualDeviceState"/>.
/// <para>
/// Inbound traffic (acknowledgements and the simulator's input frames) is delivered on one
/// background reader, in order, like the real read loop — never on the sender's thread.
/// </para>
/// </summary>
public sealed class VirtualSerialConnection(string portName, VirtualDeviceState state) : ISerialConnection
{
    /// <summary>Firmware version a virtual device reports (major, minor, patch).</summary>
    private static readonly byte[] Version = [0, 0, 1];

    private Channel<byte[]> _inbound;
    private volatile bool _ready;

    public event EventHandler<ConnectionEventArgs> Connected;
    public event EventHandler<ConnectionEventArgs> Disconnected;
    public event EventHandler<MessageEventArgs> MessageReceived;

    public bool IsReady => _ready;

    public void Connect()
    {
        if (_ready)
            throw new InvalidOperationException("Port is already open.");

        _inbound = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
        _ready = true;
        Connected?.Invoke(this, new ConnectionEventArgs(portName));
        _ = Task.Run(() => DeliverInbound(_inbound.Reader));
    }

    public void Send(ReadOnlySpan<byte> data) => Receive(data);

    public void SendMaskedInPlace(byte[] buffer, int payloadOffset, int payloadLength) =>
        Receive(buffer.AsSpan(payloadOffset, payloadLength));

    /// <summary>Queues a frame as if the device had sent it (input from the simulator).</summary>
    public void Inject(byte[] packet)
    {
        if (_ready)
            _inbound.Writer.TryWrite(packet);
    }

    public void Close()
    {
        if (!_ready)
            return;

        _ready = false;
        _inbound.Writer.TryComplete();
        Disconnected?.Invoke(this, new ConnectionEventArgs(portName));
    }

    /// <summary>Handles one command packet: [length, command, transaction id, data…]. The length
    /// byte is capped at 0xFF for large frames, so the span's own length is what counts.</summary>
    private void Receive(ReadOnlySpan<byte> packet)
    {
        if (!_ready || packet.Length < 3)
            return;

        var command = (Constants.Command)packet[1];
        byte transactionId = packet[2];
        ReadOnlySpan<byte> data = packet[3..];
        byte[] reply = [];

        switch (command)
        {
            case Constants.Command.FRAMEBUFF:
                state.WriteFramebuffer(data);
                break;
            case Constants.Command.DRAW:
                state.Present(data);
                break;
            case Constants.Command.SET_COLOR when data.Length >= 4:
                state.SetLed(data[0], data[1], data[2], data[3]);
                break;
            case Constants.Command.SET_BRIGHTNESS when data.Length >= 1:
                state.SetBrightness(data[0]);
                break;
            case Constants.Command.SET_VIBRATION:
                state.Vibrate();
                break;
            case Constants.Command.SERIAL:
                reply = Encoding.ASCII.GetBytes(Utils.VirtualDevice.Serial);
                break;
            case Constants.Command.VERSION:
                reply = Version;
                break;
        }

        // Acknowledge every command, as the firmware does; a fire-and-forget sender has
        // already completed and simply finds no pending transaction for the id.
        byte[] ack = new byte[3 + reply.Length];
        ack[0] = (byte)Math.Min(ack.Length, 0xFF);
        ack[1] = (byte)command;
        ack[2] = transactionId;
        reply.CopyTo(ack, 3);
        _inbound.Writer.TryWrite(ack);
    }

    private async Task DeliverInbound(ChannelReader<byte[]> reader)
    {
        await foreach (byte[] message in reader.ReadAllAsync())
        {
            try
            {
                MessageReceived?.Invoke(this, new MessageEventArgs(message));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VirtualDevice] Handling an inbound frame failed: {ex.Message}");
            }
        }
    }
}
