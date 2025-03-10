using System.IO.Ports;
using System.Text;

namespace LoupixDeck.LoupedeckDevice.Serial
{
    /// <summary>
    /// Represents a serial connection that handles a handshake and message-based communication.
    /// </summary>
    public class SerialConnection : ISerialConnection
    {
        /// <summary>
        /// HTTP request header for the WebSocket upgrade handshake.
        /// </summary>
        private const string WS_UPGRADE_HEADER =
            @"GET /index.html
HTTP/1.1
Connection: Upgrade
Upgrade: websocket
Sec-WebSocket-Key: 123abc

";

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
        public SerialConnection(string portName, int baudRate = 256000)
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

                // Perform the handshake synchronously.
                PerformHandshake();

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
                // Similar to the Node.js code:
                // - Magic byte 0x82
                // - Length information, depending on the total size.
                if (data.Length > 0xFF)
                {
                    // 14-byte header:
                    // [0] = 0x82, [1] = 0xFF, [6..9] = packet length (Big Endian)
                    var header = new byte[14];
                    header[0] = 0x82;
                    header[1] = 0xFF;
                    WriteUInt32Be(header, 6, (uint)data.Length);
                    // Adjust the remaining part of the header as needed, if your protocol requires it.
                    _serialPort?.Write(header, 0, header.Length);
                }
                else
                {
                    // 6-byte header:
                    // [0] = 0x82, [1] = 0x80 + length
                    var header = new byte[6];
                    header[0] = 0x82;
                    header[1] = (byte)(0x80 + data.Length);
                    _serialPort?.Write(header, 0, header.Length);
                }

                // Write payload
                _serialPort?.Write(data, 0, data.Length);
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
                _serialPort.Dispose();
                _serialPort = null;
            }

            Disconnected?.Invoke(this, new ConnectionEventArgs(_portName));
        }

        /// <summary>
        /// Performs a "GET ... websocket" handshake and checks for the expected "HTTP/1.1" response.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if the serial port is not initialized.</exception>
        /// <exception cref="IOException">Thrown if no response arrives or an invalid response is received.</exception>
        private void PerformHandshake()
        {
            // First, write the header
            var buffer = Encoding.ASCII.GetBytes(WS_UPGRADE_HEADER);
            _serialPort?.BaseStream.Write(buffer, 0, buffer.Length);

            // Then read the response synchronously
            var readBuf = new byte[1024];
            if (_serialPort == null)
            {
                throw new InvalidOperationException("Serial port is not initialized.");
            }

            _serialPort.ReadTimeout = 2000;

            // Toggling DTR might reset some devices and allow them to respond correctly.
            _serialPort.DtrEnable = false;
            Thread.Sleep(100);
            _serialPort.DtrEnable = true;

            int read = _serialPort.BaseStream.Read(readBuf, 0, readBuf.Length);
            if (read <= 0)
            {
                throw new IOException("No response received during the handshake.");
            }

            var response = Encoding.ASCII.GetString(readBuf, 0, read);
            if (!response.StartsWith(WS_UPGRADE_RESPONSE, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException($"Invalid handshake response: {response}");
            }

            _serialPort.ReadTimeout = SerialPort.InfiniteTimeout;
        }

        /// <summary>
        /// Thread routine that continuously reads incoming data. 
        /// Once complete packets are detected, triggers the <see cref="MessageReceived"/> event.
        /// </summary>
        private void ReadLoop()
        {
            // The MagicByteLengthParser identifies packets that start with a magic byte (0x82)
            // and then extracts the complete payload based on the length specified.
            var parser = new MagicByteLengthParser(0x82);
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
                    var packets = parser.ProcessData(buf, read);
                    foreach (var packet in packets)
                    {
                        // Raise event for each complete packet
                        MessageReceived?.Invoke(this, new MessageEventArgs(packet));
                    }
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

    }
}
