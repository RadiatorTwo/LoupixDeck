using System.IO.Ports;
using System.Text;

namespace LoupixDeck.LoupedeckDevice.Serial;

/// <summary>
/// <see cref="ISerialTransport"/> over <see cref="SerialPort"/>. Used on Windows, where the
/// stream is event-driven and costs nothing while idle.
/// </summary>
internal sealed class SerialPortTransport(string portName, int baudRate) : ISerialTransport
{
    private SerialPort _serialPort = new(portName, baudRate)
    {
        Parity = Parity.None,
        DataBits = 8,
        StopBits = StopBits.One,
        Handshake = Handshake.None,
        ReadTimeout = SerialPort.InfiniteTimeout,
        WriteTimeout = 3000,
        Encoding = Encoding.UTF8
    };

    public bool IsOpen => _serialPort is { IsOpen: true };

    public void Open() => _serialPort.Open();

    public int Read(byte[] buffer, int offset, int count, int timeoutMs)
    {
        var port = _serialPort;
        if (port is not { IsOpen: true })
            return 0;

        // Only touch the timeout when it changes: on Windows every set is a SetCommTimeouts call.
        if (port.ReadTimeout != timeoutMs)
            port.ReadTimeout = timeoutMs;

        return port.BaseStream.Read(buffer, offset, count);
    }

    public void Write(byte[] buffer, int offset, int count) => _serialPort?.Write(buffer, offset, count);

    public void Close()
    {
        var port = _serialPort;
        if (port == null)
            return;

        try
        {
            if (port.IsOpen)
            {
                port.Close();
            }
        }
        catch
        {
            // Optionally log or handle close exceptions.
        }
        finally
        {
            try
            {
                // On Linux, SerialPort.Dispose() calls SerialStream.Flush() ->
                // Termios.TermiosDrain() on the SafeSerialDeviceHandle. During shutdown
                // the handle may already be disposed (the ReadLoop thread races with the
                // main-thread teardown and calls Close() from its own finally block), so
                // the drain throws ObjectDisposedException. Because this runs on the
                // background ReadLoop thread, an unguarded throw becomes an unhandled
                // exception that terminates the whole process on exit. Swallow it.
                port.Dispose();
            }
            catch
            {
                // Handle already gone (device unplugged / concurrent Close) — ignore.
            }

            _serialPort = null;
        }
    }
}
