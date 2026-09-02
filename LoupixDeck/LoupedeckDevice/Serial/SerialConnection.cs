using System.Buffers;
using System.Buffers.Binary;
using System.IO.Ports;
using System.Numerics.Tensors;
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
    /// <param name="baudRate">The baud rate. Defaults to
    /// <see cref="Constants.DefaultBaudrate"/> if not specified.</param>
    public SerialConnection(string portName, int baudRate = Constants.DefaultBaudrate)
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
    /// Bytes occupied by a masked binary WebSocket header (opcode + length + 4-byte mask)
    /// for a payload of <paramref name="payloadLength"/> bytes. FRAMEBUFF writers reserve
    /// this many bytes in front of the command packet so masking can happen in place.
    /// </summary>
    public static int MaskedHeaderLength(int payloadLength)
    {
        if (payloadLength <= 125) return 6;
        if (payloadLength <= 0xFFFF) return 8;
        return 14;
    }

    /// <summary>
    /// Sends data over the serial connection as a masked binary WebSocket frame
    /// (RFC 6455 §5.2/§5.3). Newer Loupedeck firmware (>=0.2.26) rejects unmasked frames.
    /// </summary>
    public void Send(ReadOnlySpan<byte> data)
    {
        if (!IsReady)
            return;

        int payloadLength = data.Length;
        int headerLength = MaskedHeaderLength(payloadLength);
        int frameLength = headerLength + payloadLength;
        byte[] frame = ArrayPool<byte>.Shared.Rent(frameLength);
        try
        {
            WriteMaskedFrame(frame.AsSpan(0, frameLength), data, payloadLength);
            _serialPort?.Write(frame, 0, frameLength);
        }
        catch (Exception ex)
        {
            Disconnected?.Invoke(this, new ConnectionEventArgs(_portName, ex));
            Close();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(frame);
        }
    }

    /// <inheritdoc />
    public void SendMaskedInPlace(byte[] buffer, int payloadOffset, int payloadLength)
    {
        if (!IsReady)
            return;

        ArgumentNullException.ThrowIfNull(buffer);

        int headerLength = MaskedHeaderLength(payloadLength);
        if (payloadOffset < headerLength)
            throw new ArgumentException("Buffer has no reserved WebSocket header prefix.", nameof(payloadOffset));
        if (payloadOffset + payloadLength > buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(payloadLength));

        int start = payloadOffset - headerLength;
        int frameLength = headerLength + payloadLength;
        try
        {
            WriteMaskedFrame(
                buffer.AsSpan(start, frameLength),
                buffer.AsSpan(payloadOffset, payloadLength),
                payloadLength);
            _serialPort?.Write(buffer, start, frameLength);
        }
        catch (Exception ex)
        {
            Disconnected?.Invoke(this, new ConnectionEventArgs(_portName, ex));
            Close();
        }
    }

    /// <summary>
    /// Builds a masked binary WebSocket frame in <paramref name="frame"/>. The payload
    /// region of <paramref name="frame"/> may alias <paramref name="payload"/> (in-place).
    /// </summary>
    private static void WriteMaskedFrame(Span<byte> frame, ReadOnlySpan<byte> payload, int payloadLength)
    {
        int maskOffset;
        frame[0] = 0x82;
        if (payloadLength <= 125)
        {
            frame[1] = (byte)(0x80 | payloadLength);
            maskOffset = 2;
        }
        else if (payloadLength <= 0xFFFF)
        {
            frame[1] = 0xFE;
            BinaryPrimitives.WriteUInt16BigEndian(frame.Slice(2), (ushort)payloadLength);
            maskOffset = 4;
        }
        else
        {
            frame[1] = 0xFF;
            BinaryPrimitives.WriteUInt64BigEndian(frame.Slice(2), (ulong)payloadLength);
            maskOffset = 10;
        }

        Span<byte> mask = frame.Slice(maskOffset, 4);
        RandomNumberGenerator.Fill(mask);

        Span<byte> payloadDest = frame.Slice(maskOffset + 4, payloadLength);
        XorRepeatingMask(payload, mask, payloadDest);
    }

    /// <summary>
    /// XOR <paramref name="source"/> with a repeating 4-byte mask into <paramref name="destination"/>.
    /// Source and destination may be the same span. The mask is tiled to 256 bytes so
    /// <see cref="TensorPrimitives.Xor{T}(ReadOnlySpan{T}, ReadOnlySpan{T}, Span{T})"/> runs as SIMD.
    /// </summary>
    private static void XorRepeatingMask(ReadOnlySpan<byte> source, ReadOnlySpan<byte> mask4, Span<byte> destination)
    {
        Span<byte> tiled = stackalloc byte[256];
        byte m0 = mask4[0], m1 = mask4[1], m2 = mask4[2], m3 = mask4[3];
        for (int i = 0; i < tiled.Length; i += 4)
        {
            tiled[i] = m0;
            tiled[i + 1] = m1;
            tiled[i + 2] = m2;
            tiled[i + 3] = m3;
        }

        int offset = 0;
        while (offset + tiled.Length <= source.Length)
        {
            TensorPrimitives.Xor(source.Slice(offset, tiled.Length), tiled, destination.Slice(offset, tiled.Length));
            offset += tiled.Length;
        }

        int remaining = source.Length - offset;
        if (remaining > 0)
            TensorPrimitives.Xor(source.Slice(offset, remaining), tiled.Slice(0, remaining), destination.Slice(offset, remaining));
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

}
