using System.IO.Ports;
using System.Security.Cryptography;
using System.Text;

namespace LoupixDeck.LoupedeckDevice.Serial;

/// <summary>
/// Represents a serial connection that handles a handshake and message-based communication.
/// </summary>
public class SerialConnection : ISerialConnection
{
    /// <summary>
    /// HTTP request header for the WebSocket upgrade handshake.
    /// </summary>
    private const string WS_UPGRADE_HEADER =
        "GET /index.html HTTP/1.1\r\n" +
        "Connection: Upgrade\r\n" +
        "Upgrade: websocket\r\n" +
        "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n" +
        "Sec-WebSocket-Version: 13\r\n" +
        "\r\n";

    /// <summary>
    /// Partial expected response from the device to confirm the handshake.
    /// </summary>
    private const string WS_UPGRADE_RESPONSE = "HTTP/1.1";

    /// <summary>
    /// Name of the serial port to connect to.
    /// </summary>
    private readonly string _portName;

    /// <summary>
    /// Baud rate for the serial port connection.
    /// </summary>
    private readonly int _baudRate;

    /// <summary>
    /// SerialPort instance used for communication.
    /// </summary>
    private SerialPort _serialPort;

    /// <summary>
    /// Thread that continuously reads incoming data.
    /// </summary>
    private Thread _readThread = null!;

    /// <summary>
    /// Controls whether the reading thread is running.
    /// </summary>
    private volatile bool _running;

    /// <summary>
    /// Fired when the connection has been successfully established.
    /// </summary>
    public event EventHandler<ConnectionEventArgs> Connected = null!;

    /// <summary>
    /// Fired when the connection is lost or closed (including errors).
    /// </summary>
    public event EventHandler<ConnectionEventArgs> Disconnected = null!;

    /// <summary>
    /// Fired when a complete message has been received.
    /// </summary>
    public event EventHandler<MessageEventArgs> MessageReceived = null!;

    /// <summary>
    /// Initializes a new instance of the <see cref="SerialConnection"/> class.
    /// </summary>
    /// <param name="portName">The name of the serial port to connect to.</param>
    /// <param name="baudRate">The baud rate. Defaults to 256000 if not specified.</param>
    public SerialConnection(string portName, int baudRate = 921600)
    {
        _portName = portName;
        _baudRate = baudRate;
    }

    /// <summary>
    /// Indicates whether the serial port is open and ready for communication.
    /// </summary>
    public bool IsReady => _serialPort is not null && _serialPort.IsOpen;
    
    /// <summary>
    /// Searches for all available serial ports and returns them as a list.
    /// (Optional: Not part of the interface, but useful for a discovery-like feature.)
    /// </summary>
    public static List<string> DiscoverPorts()
    {
        return new List<string>(SerialPort.GetPortNames());
    }

    /// <summary>
    /// Establishes the connection and performs the handshake. 
    /// Afterwards, starts a thread that continuously parses and reads incoming data.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if the port is already open.</exception>
    /// <exception cref="Exception">Thrown if an error occurs during connection or handshake.</exception>
    public void Connect()
    {
        if (IsReady)
        {
            throw new InvalidOperationException("Port is already open.");
        }

        try
        {
            _serialPort = new SerialPort(_portName, _baudRate)
            {
                Parity = Parity.None,
                DataBits = 8,
                StopBits = StopBits.One,
                Handshake = Handshake.None,
                ReadTimeout = SerialPort.InfiniteTimeout,
                WriteTimeout = 3000,
                Encoding = Encoding.UTF8
            };

            _serialPort.Open();

            // Perform the handshake to get the device into Websocket mode on the Serial Port
            if (!PerformHandshake())
            {
                throw new IOException("Handshake failed after multiple attempts.");
            }

            // If the handshake is successful, notify that we have connected.
            Connected?.Invoke(this, new ConnectionEventArgs(_portName));

            // Start the thread that reads incoming data and raises the MessageReceived event.
            _running = true;

            _readThread = new Thread(ReadLoop)
            {
                IsBackground = true
            };
            _readThread.Start();
        }
        catch (Exception ex)
        {
            // Surface the cause in the (startup) log. This path used to be silent,
            // so a port that could not be opened — missing udev rule, user not in
            // the 'dialout' group, or the port already in use — produced an empty
            // log and no error window (issue #146).
            string hint = ex switch
            {
                UnauthorizedAccessException =>
                    " (permission denied — check the udev rule / 'dialout' group membership, or the port is already in use)",
                _ => string.Empty
            };
            Console.WriteLine($"[Serial] Failed to open '{_portName}' @ {_baudRate}: {ex.Message}{hint}");

            // If something fails, close the port immediately.
            if (_serialPort != null && _serialPort.IsOpen)
            {
                _serialPort.Close();
            }
            _serialPort = null;

            // We do not have an error event in the interface, so we use Disconnected to indicate a failure.
            Disconnected?.Invoke(this, new ConnectionEventArgs(_portName, ex));

            // Rethrow the exception if needed.
            throw;
        }
    }

    /// <summary>
    /// Sends data over the serial connection (including a header).
    /// In the Node.js equivalent code, there is a distinction between raw and non-raw data.
    /// This interface only provides Send(byte[] data), so a header is always included.
    /// If you need a raw send, the interface would need to be adapted.
    /// </summary>
    /// <param name="data">The data to be sent.</param>
    public void Send(byte[] data)
    {
        if (!IsReady)
        {
            return;
        }

        try
        {
            // Binary WebSocket frame (opcode 0x2). As a client we MUST mask the
            // payload (RFC 6455 §5.2/§5.3): a 4-byte mask key follows the length
            // field and each payload byte is XOR'd with mask[i % 4]. The mask bit
            // (0x80) is set in the length byte. Newer Loupedeck firmware (>=0.2.26)
            // rejects unmasked frames, so the whole frame is written in one call.
            var mask = RandomNumberGenerator.GetBytes(4);
            var maskedPayload = new byte[data.Length];
            for (var i = 0; i < data.Length; i++)
                maskedPayload[i] = (byte)(data[i] ^ mask[i % 4]);

            byte[] frame;
            if (data.Length <= 125)
            {
                // 2-byte header + 4-byte mask + payload.
                frame = new byte[6 + data.Length];
                frame[0] = 0x82;
                frame[1] = (byte)(0x80 | data.Length);
                Buffer.BlockCopy(mask, 0, frame, 2, 4);
                Buffer.BlockCopy(maskedPayload, 0, frame, 6, data.Length);
            }
            else if (data.Length <= 0xFFFF)
            {
                // 0xFE = masked + 16-bit extended length.
                frame = new byte[8 + data.Length];
                frame[0] = 0x82;
                frame[1] = 0xFE;
                frame[2] = (byte)((data.Length >> 8) & 0xFF);
                frame[3] = (byte)(data.Length & 0xFF);
                Buffer.BlockCopy(mask, 0, frame, 4, 4);
                Buffer.BlockCopy(maskedPayload, 0, frame, 8, data.Length);
            }
            else
            {
                // 0xFF = masked + 64-bit extended length.
                frame = new byte[14 + data.Length];
                frame[0] = 0x82;
                frame[1] = 0xFF;
                WriteUInt64Be(frame, 2, (ulong)data.Length);
                Buffer.BlockCopy(mask, 0, frame, 10, 4);
                Buffer.BlockCopy(maskedPayload, 0, frame, 14, data.Length);
            }

            _serialPort?.Write(frame, 0, frame.Length);
        }
        catch (Exception ex)
        {
            // On send error, trigger Disconnected or handle as appropriate.
            Disconnected?.Invoke(this, new ConnectionEventArgs(_portName, ex));
            Close();
        }
    }

    /// <summary>
    /// Closes the connection and stops the reading thread.
    /// </summary>
    public void Close()
    {
        if (_serialPort == null)
        {
            return;
        }

        _running = false;

        try
        {
            if (_serialPort.IsOpen)
            {
                _serialPort.Close();
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
                _serialPort?.Dispose();
            }
            catch
            {
                // Handle already gone (device unplugged / concurrent Close) — ignore.
            }

            _serialPort = null;
        }

        Disconnected?.Invoke(this, new ConnectionEventArgs(_portName));
    }

    /// <summary>
    /// Attempts to perform a GET ... websocket handshake and checks for the expected HTTP/1.1 response.
    /// Makes several attempts if the handshake fails.
    /// </summary>
    /// <param name="maxRetries">The maximum number of attempts.</param>
    /// <returns>Returns true if the handshake was successful, otherwise false.</returns>
    private bool PerformHandshake(int maxRetries = 3)
    {
        var buffer = Encoding.ASCII.GetBytes(WS_UPGRADE_HEADER);

        if (_serialPort == null)
        {
            throw new InvalidOperationException("Serial port is not initialized.");
        }

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                // SendWakeSignal();

                // Sending Header
                _serialPort.BaseStream.Write(buffer, 0, buffer.Length);
                _serialPort.BaseStream.Flush();

                // Read answer.
                // The first handshake after a (re)start always times out — the initial
                // header write wakes/resets the device and it only replies on the second
                // attempt. So this timeout is paid in full on every startup; it must stay
                // small. When the device DOES reply, Read returns immediately, so the value
                // only bounds the wait on the guaranteed-silent first attempt.
                _serialPort.ReadTimeout = 250; // Timeout for the handshake response
                var readBuf = new byte[1024];
                var responseBuilder = new StringBuilder();

                while (true)
                {
                    int read = _serialPort.BaseStream.Read(readBuf, 0, readBuf.Length);
                    if (read > 0)
                    {
                        responseBuilder.Append(Encoding.ASCII.GetString(readBuf, 0, read));

                        // Check whether the response begins with the expected header
                        if (responseBuilder.Length >= WS_UPGRADE_RESPONSE.Length)
                        {
                            var response = responseBuilder.ToString();
                            if (response.StartsWith(WS_UPGRADE_RESPONSE, StringComparison.OrdinalIgnoreCase))
                            {
                                // Successful handshake
                                return true;
                            }
                            else
                            {
                                throw new IOException($"Invalid handshake response: {response}");
                            }
                        }
                    }
                    else
                    {
                        throw new IOException("No response received during the handshake.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Handshake attempt {attempt} failed: {ex.Message}");

                // Last attempt failed
                if (attempt == maxRetries)
                {
                    return false;
                }

                Thread.Sleep(500);
            }
            finally
            {
                // Reset timeout
                _serialPort.ReadTimeout = SerialPort.InfiniteTimeout;
            }
        }

        return false; // Should never be reached
    }

    private void SendWakeSignal()
    {
        try
        {
            // Send a zero byte (0x00) as a wake-up signal
            var wakeSignal = "\0"u8.ToArray();
            //var wakeSignal = Encoding.ASCII.GetBytes("HELO");
            _serialPort.BaseStream.Write(wakeSignal, 0, wakeSignal.Length);

            // Optional: Kurze Pause, um dem Ger�t Zeit zu geben, zu reagieren
            Thread.Sleep(100);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to send wake signal: {ex.Message}");
        }
    }

    /// <summary>
    /// Thread routine that continuously reads incoming data. 
    /// Once complete packets are detected, triggers the <see cref="MessageReceived"/> event.
    /// </summary>
    private void ReadLoop()
    {
        // The MagicByteLengthParser identifies packets that start with a magic byte (0x82)
        // and then extracts the complete payload based on the length specified.
        var parser = new SerialDataParser();
        parser.PacketReceived += packet =>
        {
            MessageReceived?.Invoke(this, new MessageEventArgs(packet));
        };

        var buf = new byte[1024];

        try
        {
            while (_running && _serialPort != null && _serialPort.IsOpen)
            {
                int read = _serialPort.BaseStream.Read(buf, 0, buf.Length);
                if (read <= 0)
                {
                    // Port is closed or EOF
                    break;
                }

                // Pass the newly read data to the parser
                parser.ProcessReceivedData(buf, read);
            }
        }
        catch (Exception ex)
        {
            // Only notify if we have not explicitly closed the port
            if (_running)
            {
                Disconnected?.Invoke(this, new ConnectionEventArgs(_portName, ex));
            }
        }
        finally
        {
            // Ensure the connection is closed in any case
            Close();
        }
    }

    /// <summary>
    /// Writes a 32-bit unsigned integer into the specified buffer using big-endian format.
    /// </summary>
    /// <param name="buffer">The target buffer.</param>
    /// <param name="startIndex">Position to begin writing in the buffer.</param>
    /// <param name="value">The 32-bit unsigned integer value.</param>
    private static void WriteUInt32Be(byte[] buffer, int startIndex, uint value)
    {
        buffer[startIndex] = (byte)((value >> 24) & 0xFF);
        buffer[startIndex + 1] = (byte)((value >> 16) & 0xFF);
        buffer[startIndex + 2] = (byte)((value >> 8) & 0xFF);
        buffer[startIndex + 3] = (byte)(value & 0xFF);
    }

    /// <summary>
    /// Writes a 64-bit unsigned integer into the specified buffer using big-endian format.
    /// Used for WebSocket frames whose payload exceeds 0xFFFF bytes.
    /// </summary>
    /// <param name="buffer">The target buffer.</param>
    /// <param name="startIndex">Position to begin writing in the buffer.</param>
    /// <param name="value">The 64-bit unsigned integer value.</param>
    private static void WriteUInt64Be(byte[] buffer, int startIndex, ulong value)
    {
        buffer[startIndex] = (byte)((value >> 56) & 0xFF);
        buffer[startIndex + 1] = (byte)((value >> 48) & 0xFF);
        buffer[startIndex + 2] = (byte)((value >> 40) & 0xFF);
        buffer[startIndex + 3] = (byte)((value >> 32) & 0xFF);
        buffer[startIndex + 4] = (byte)((value >> 24) & 0xFF);
        buffer[startIndex + 5] = (byte)((value >> 16) & 0xFF);
        buffer[startIndex + 6] = (byte)((value >> 8) & 0xFF);
        buffer[startIndex + 7] = (byte)(value & 0xFF);
    }

}
