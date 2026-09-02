namespace LoupixDeck.LoupedeckDevice.Serial;

public interface ISerialConnection
{
    event EventHandler<ConnectionEventArgs> Connected;
    event EventHandler<ConnectionEventArgs> Disconnected;
    event EventHandler<MessageEventArgs> MessageReceived;
    void Connect();
    bool IsReady { get; }
    void Send(ReadOnlySpan<byte> data);
    /// <summary>
    /// Writes a masked WebSocket frame using <paramref name="buffer"/> in place.
    /// Bytes immediately before <paramref name="payloadOffset"/> must have room
    /// for <see cref="SerialConnection.MaskedHeaderLength"/>; the payload is XOR-masked in place.
    /// </summary>
    void SendMaskedInPlace(byte[] buffer, int payloadOffset, int payloadLength);
    void Close();
}