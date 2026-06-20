namespace LoupixDeck.LoupedeckDevice;

public class ConnectionEventArgs(string portName, Exception error = null) : EventArgs
{
    public string PortName { get; set; } = portName;
    public Exception Error { get; set; } = error ?? new ArgumentNullException(nameof(error));
}