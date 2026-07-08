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

    /// <summary>
    /// Optional per-connection write observer for the display-transfer benchmark (spike).
    /// When set, the transport times each blocking write and reports
    /// (payloadBytes, onWireBytes, writeDuration). Null (default) = no instrumentation,
    /// unchanged fast path. Kept per-connection so parallel per-device runs stay isolated.
    /// </summary>
    Action<int, int, TimeSpan> WriteObserver { get; set; }
}