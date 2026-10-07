namespace LoupixDeck.LoupedeckDevice.Serial;

/// <summary>
/// The byte pipe underneath <see cref="SerialConnection"/>: open, read, write, close.
/// Framing, the handshake and reconnect handling stay in <see cref="SerialConnection"/>.
/// </summary>
internal interface ISerialTransport
{
    bool IsOpen { get; }

    /// <summary>
    /// Opens the port. Failures surface as <see cref="UnauthorizedAccessException"/> (in use or
    /// no permission), <see cref="FileNotFoundException"/> (no such port) or <see cref="IOException"/>,
    /// the same types <see cref="System.IO.Ports.SerialPort.Open"/> raises.
    /// </summary>
    void Open();

    /// <summary>
    /// Blocks until at least one byte arrives. Returns the number of bytes read, or 0 once the
    /// transport has been closed. Throws <see cref="TimeoutException"/> when
    /// <paramref name="timeoutMs"/> (or <see cref="Timeout.Infinite"/>) elapses without data.
    /// </summary>
    int Read(byte[] buffer, int offset, int count, int timeoutMs);

    void Write(byte[] buffer, int offset, int count);

    /// <summary>Closes the port and wakes a blocked <see cref="Read"/>. Never throws.</summary>
    void Close();

    /// <summary>
    /// The transport for the current platform. On macOS and Linux this is
    /// <see cref="PosixSerialTransport"/>: System.IO.Ports' Unix stream polls the port every
    /// millisecond for as long as it is open, which costs a few percent CPU on an idle deck.
    /// Setting LOUPIXDECK_LEGACY_SERIAL=1 goes back to <see cref="System.IO.Ports.SerialPort"/>.
    /// </summary>
    static ISerialTransport Create(string portName, int baudRate)
    {
#if !WINDOWS
        if ((OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
            && Environment.GetEnvironmentVariable("LOUPIXDECK_LEGACY_SERIAL") != "1")
        {
            return new PosixSerialTransport(portName, baudRate);
        }
#endif
        return new SerialPortTransport(portName, baudRate);
    }
}
