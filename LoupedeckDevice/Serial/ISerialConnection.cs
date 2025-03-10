namespace LoupixDeck.LoupedeckDevice.Serial;

public interface ISerialConnection
{
    event EventHandler<ConnectionEventArgs> Connected;
    event EventHandler<ConnectionEventArgs> Disconnected;
    event EventHandler<MessageEventArgs> MessageReceived;
    void Connect();
    bool IsReady { get; }
    void Send(byte[] data);
    void Close();
}