using System.Threading.Channels;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using LoupixDeck.LoupedeckDevice.Serial;
using LoupixDeck.Models;
using LoupixDeck.Utils;
using SkiaSharp;

namespace LoupixDeck.LoupedeckDevice.Device;

/// <summary>
/// Base class for Loupedeck devices.
/// Contains all functionalities (connection, sending/receiving, button, rotation, and touch events, drawing, etc.).
/// </summary>
public class LoupedeckDevice
{
    private ISerialConnection _connection;
    private byte _transactionId;

    private record QueueItem(
        Constants.Command Command,
        byte[] Data,
        TaskCompletionSource<byte[]> Completion,
        bool ExpectResponse,
        CancellationTokenSource TimeoutCts);

    private readonly Channel<QueueItem> _sendChannel = Channel.CreateUnbounded<QueueItem>();

    private readonly Dictionary<byte, TaskCompletionSource<byte[]>> _pendingTransactions = new();
    private readonly Dictionary<byte, CancellationTokenSource> _pendingTimeouts = new();
    private readonly Dictionary<byte, TouchInfo> _touches = new();

    private int ReconnectInterval { get; set; }
    public string Host { get; set; }
    private string Path { get; set; }
    private int Baudrate { get; set; }

    protected Dictionary<string, DisplayInfo> Displays { get; init; } = new();
    public int[] Buttons { get; set; }
    protected int Columns { get; init; }
    protected int Rows { get; init; }
    protected int[] VisibleX { get; init; }
    protected int[] VisibleY { get; init; }
    public string Type { get; set; }
    public string ProductId { get; set; }

    public event EventHandler<ConnectionEventArgs> OnConnect;
    public event EventHandler<ConnectionEventArgs> OnDisconnect;
    public event EventHandler<ButtonEventArgs> OnButton; // "down" or "up"
    public event EventHandler<RotateEventArgs> OnRotate;
    public event EventHandler<TouchEventArgs> OnTouch;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoupedeckDevice"/> class.
    /// </summary>
    /// <param name="host">Host name or IP (if applicable).</param>
    /// <param name="path">Device path (e.g. serial port).</param>
    /// <param name="baudrate">Device Connection Baudrate</param>
    /// <param name="autoConnect">If true, attempts to connect automatically.</param>
    /// <param name="reconnectInterval">Interval (ms) to wait before reconnecting.</param>
    protected LoupedeckDevice(string host = null, string path = null, int baudrate = 0, bool autoConnect = true,
        int reconnectInterval = Constants.DefaultReconnectInterval)
    {
        Host = host;
        Path = path;
        ReconnectInterval = reconnectInterval;
        Baudrate = baudrate > 0 ? baudrate : 115200;
        if (autoConnect)
        {
            ConnectBlind();
        }
    }

    /// <summary>
    /// Attempts to connect without throwing exceptions; errors are reported via the Disconnect event.
    /// </summary>
    private void ConnectBlind()
    {
        try
        {
            Connect();
        }
        catch
        {
            // Errors are reported in the Disconnect event
        }
    }

    /// <summary>
    /// Connects to the device, either via the specified path or by discovering available devices.
    /// </summary>
    private void Connect()
    {
        if (!string.IsNullOrEmpty(Path))
        {
            _connection = new SerialConnection(Path, Baudrate);
        }
        else
        {
            if (!string.IsNullOrEmpty(Path) && Baudrate > 0)
            {
                _connection = new SerialConnection(Path, Baudrate);
            }
            else
            {
                OnDisconnect?.Invoke(this, new ConnectionEventArgs("N/A", new Exception("Device path is null")));
                return;
            }

            if (_connection == null)
            {
                OnDisconnect?.Invoke(this, new ConnectionEventArgs("N/A", new Exception("No device found")));
                return;
            }
        }

        _connection.Connected += (_, e) => OnConnect?.Invoke(this, e);
        _connection.MessageReceived += (_, e) => OnReceive(e.Data);
        _connection.Disconnected += (_, e) =>
        {
            OnDisconnect?.Invoke(this, e);
            Thread.Sleep(ReconnectInterval);
            ConnectBlind();
        };

        _connection.Connect();

        StartQueueWorker();
    }

    /// <summary>
    /// Starts the background worker that processes queued send requests sequentially.
    /// This ensures that all communication with the device is serialized and thread-safe.
    /// </summary>
    private void StartQueueWorker()
    {
        _ = Task.Run(async () =>
        {
            await foreach (var item in _sendChannel.Reader.ReadAllAsync())
            {
                try
                {
                    _transactionId = (byte)((_transactionId + 1) % 256);
                    if (_transactionId == 0)
                        _transactionId++;

                    var length = (byte)Math.Min(3 + item.Data.Length, 0xff);
                    byte[] header = [length, (byte)item.Command, _transactionId];
                    var packet = header.Concat(item.Data).ToArray();

                    if (item.ExpectResponse)
                    {
                        _pendingTransactions[_transactionId] = item.Completion;
                        _pendingTimeouts[_transactionId] = item.TimeoutCts;
                    }

                    _connection?.Send(packet);

                    if (!item.ExpectResponse)
                    {
                        // Immediately complete the task if no response is expected
                        item.Completion.TrySetResult([]);
                    }
                }
                catch (Exception ex)
                {
                    item.Completion.TrySetException(ex);
                }
            }
        });
    }

    /// <summary>
    /// Closes the current connection.
    /// </summary>
    public void Close()
    {
        _sendChannel.Writer.TryComplete();
        _connection?.Close();
    }

    /// <summary>
    /// Queues a command and optional data to be sent to the device, 
    /// and asynchronously waits for the response.
    /// </summary>
    /// <param name="command">The command to send to the device.</param>
    /// <param name="data">Optional payload data for the command.</param>
    /// <returns>A task that completes with the device's response payload.</returns>
    private async Task<byte[]> SendAsync(Constants.Command command, byte[] data = null)
    {
        data ??= [];

        var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

        timeoutCts.Token.Register(() =>
        {
            tcs.TrySetException(new TimeoutException($"Timeout waiting for response to command {command}."));
        });

        var item = new QueueItem(command, data, tcs, true, timeoutCts);

        // The cancellation token is not used for the write operation:
        // ReSharper disable once MethodSupportsCancellation
        await _sendChannel.Writer.WriteAsync(item);

        return await tcs.Task;
    }

    /// <summary>
    /// Queues a command and optional data to be sent to the device without waiting for a response.
    /// Used for fire-and-forget operations where no reply is expected.
    /// </summary>
    /// <param name="command">The command to send to the device.</param>
    /// <param name="data">Optional payload data for the command.</param>
    /// <returns>A task that completes when the command has been sent.</returns>
    private async Task SendNoResponseAsync(Constants.Command command, byte[] data = null)
    {
        data ??= [];

        var dummyCts = new CancellationTokenSource();
        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

        var item = new QueueItem(command, data, tcs, false, dummyCts);

        // The cancellation token is not used for the write operation:
        // ReSharper disable once MethodSupportsCancellation
        await _sendChannel.Writer.WriteAsync(item);
    }


    /// <summary>
    /// Sends a command with the given data and waits synchronously for the response.
    /// Frame format: [length (1 byte), command (1 byte), transactionID (1 byte), data]
    /// </summary>
    private byte[] Send(Constants.Command command, byte[] data = null)
    {
        return SendAsync(command, data).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Sends a command with the given data but does not wait for a response.
    /// </summary>
    private void SendNoResponse(Constants.Command command, byte[] data = null)
    {
        SendNoResponseAsync(command, data).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Handles incoming data packets, dispatching them based on the command byte.
    /// </summary>
    private void OnReceive(byte[] buff)
    {
        if (buff.Length < 3) return;

        var msgLength = buff[0];
        var command = buff[1];
        var transactionId = buff[2];
        var payload = buff.Skip(3).Take(msgLength - 3).ToArray();

        if (_pendingTransactions.TryGetValue(transactionId, out var transaction))
        {
            transaction.SetResult(payload);
            _pendingTransactions.Remove(transactionId);
        }

        // Additionally, cancel the timeout if it exists:
        if (_pendingTimeouts.TryGetValue(transactionId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
            _pendingTimeouts.Remove(transactionId);
        }

        // Dispatch based on the received command
        switch (command)
        {
            case (byte)Constants.Command.BUTTON_PRESS:
                OnButtonPress(payload);
                break;
            case (byte)Constants.Command.KNOB_ROTATE:
                OnRotateReceived(payload);
                break;
            case (byte)Constants.Command.SERIAL:
                // Logging or other handling could happen here
                break;
            case (byte)Constants.Command.TOUCH:
                OnTouchReceived(Constants.TouchEventType.TOUCH_START, payload);
                break;
            case (byte)Constants.Command.TOUCH_END:
                OnTouchReceived(Constants.TouchEventType.TOUCH_END, payload);
                break;
            case (byte)Constants.Command.VERSION:
                // The version can be handled directly by the return value
                break;
        }
    }

    /// <summary>
    /// Handles incoming button press data.
    /// </summary>
    private void OnButtonPress(byte[] buff)
    {
        if (buff.Length < 2) return;
        var btn = buff[0];

        if (!Constants.Buttons.TryGetValue(btn, out var id)) return;

        var evt = (buff[1] == 0x00) ? Constants.ButtonEventType.BUTTON_DOWN : Constants.ButtonEventType.BUTTON_UP;
        OnButton?.Invoke(this, new ButtonEventArgs { ButtonId = id, EventType = evt });
    }

    /// <summary>
    /// Handles incoming rotation (knob) data.
    /// </summary>
    private void OnRotateReceived(byte[] buff)
    {
        if (buff.Length < 2) return;
        var btn = buff[0];
        if (!Constants.Buttons.TryGetValue(btn, out var id)) return;
        var delta = (sbyte)buff[1];
        OnRotate?.Invoke(this, new RotateEventArgs { ButtonId = id, Delta = delta });
    }

    /// <summary>
    /// Handles incoming touch data.
    /// </summary>
    private void OnTouchReceived(Constants.TouchEventType eventType, byte[] buff)
    {
        if (buff.Length < 6) return;
        var x = (buff[1] << 8) | buff[2];
        var y = (buff[3] << 8) | buff[4];
        var touchId = buff[5];

        var touch = new TouchInfo
        {
            X = x,
            Y = y,
            Id = touchId,
            Target = GetTarget(x, y)
        };

        if (eventType == Constants.TouchEventType.TOUCH_END)
        {
            _touches.Remove(touchId);
        }
        else
        {
            if (!_touches.ContainsKey(touchId))
                eventType = Constants.TouchEventType.TOUCH_START;
            _touches[touchId] = touch;
        }

        OnTouch?.Invoke(this, new TouchEventArgs
        {
            EventType = eventType,
            Touches = _touches.Values.ToList(),
            ChangedTouch = touch
        });
    }

    /// <summary>
    /// This method is overridden in derived classes to determine which area or key is touched.
    /// </summary>
    protected virtual TouchTarget GetTarget(int x, int y) => new() { Screen = "center", Key = -1 };

    /// <summary>
    /// Sends a 16-bit (5-6-5) image buffer to display "id" at the position (x,y).
    /// </summary>
    private async Task DrawBuffer(string id, int width, int height, byte[] buffer, int? x = 0, int? y = 0,
        bool autoRefresh = true)
    {
        if (Displays == null || !Displays.TryGetValue(id, out var displayInfo))
            throw new Exception($"Display '{id}' is not available on this device!");

        if (width == 0)
            width = displayInfo.Width;
        if (height == 0)
            height = displayInfo.Height;

        if (buffer.Length != width * height * 2)
            throw new Exception($"Expected buffer length of {width * height * 2}, got {buffer.Length}!");

        var header = new byte[8];

        // Write x, y, width, and height as big-endian UInt16
        if (x == null || y == null)
            throw new ArgumentNullException($"x or y cannot be null");

        header[0] = (byte)((x.Value >> 8) & 0xff);
        header[1] = (byte)(x.Value & 0xff);
        header[2] = (byte)((y.Value >> 8) & 0xff);
        header[3] = (byte)(y.Value & 0xff);
        header[4] = (byte)((width >> 8) & 0xff);
        header[5] = (byte)(width & 0xff);
        header[6] = (byte)((height >> 8) & 0xff);
        header[7] = (byte)(height & 0xff);

        var data = displayInfo.Id.Concat(header).Concat(buffer).ToArray();
        await SendAsync(Constants.Command.FRAMEBUFF, data);

        if (autoRefresh)
            await Refresh(id);
    }

    /// <summary>
    /// Creates a drawing surface with the correct dimensions, executes the callback function for drawing,
    /// and sends the resulting buffer to the device.
    /// </summary>
    /// <param name="id">Display ID.</param>
    /// <param name="width">Width (0 = use the display's default width).</param>
    /// <param name="height">Height (0 = use the display's default height).</param>
    /// <param name="bitmap">RenderTargetBitmap to be drawn</param>
    /// <param name="x">X-position in the header.</param>
    /// <param name="y">Y-position in the header.</param>
    /// <param name="autoRefresh">Should a refresh be triggered automatically?</param>
    private async Task DrawCanvas(
        string id,
        int width,
        int height,
        SKBitmap bitmap,
        int? x = 0,
        int? y = 0,
        bool autoRefresh = true)
    {
        // Determine the display
        if (Displays == null || !Displays.TryGetValue(id, out var displayInfo))
            throw new Exception($"Display '{id}' is not available on this device!");

        // If width/height = 0 => use the display's default values
        if (width == 0)
            width = displayInfo.Width;
        if (height == 0)
            height = displayInfo.Height;

        // Convert the RenderTargetBitmap into a 16-bit-5-6-5 array
        var buffer = ConvertSKBitmapToRaw16BppUnsafe(bitmap);

        // Pass the buffer to the actual DrawBuffer
        await DrawBuffer(id, width, height, buffer, x, y, autoRefresh);
    }

    /// <summary>
    /// Converts a RenderTargetBitmap (usually BGRA32) into a 16-bit-565 byte array.
    /// </summary>
    private unsafe byte[] ConvertSKBitmapToRaw16BppUnsafe(SKBitmap bitmap)
    {
        if (bitmap.ColorType != SKColorType.Bgra8888)
            throw new InvalidOperationException("Bitmap muss im Format BGRA8888 vorliegen.");

        int width = bitmap.Width;
        int height = bitmap.Height;
        int pixelCount = width * height;

        // Output-Array für 16-Bit RGB565 (2 Bytes pro Pixel)
        byte[] output = new byte[pixelCount * 2];

        // Zugriff auf die Pixeldaten als Pointer
        SKPixmap pixmap = bitmap.PeekPixels(); // Schneller Zugriff ohne Kopie
        byte* srcPtr = (byte*)pixmap.GetPixels().ToPointer();

        fixed (byte* destPtrFixed = output)
        {
            byte* destPtr = destPtrFixed;

            for (int i = 0; i < pixelCount; i++)
            {
                byte b = srcPtr[0];
                byte g = srcPtr[1];
                byte r = srcPtr[2];
                // byte a = srcPtr[3]; // optional

                // RGB888 → RGB565
                ushort r5 = (ushort)((r * 31) / 255);
                ushort g6 = (ushort)((g * 63) / 255);
                ushort b5 = (ushort)((b * 31) / 255);

                ushort rgb565 = (ushort)((r5 << 11) | (g6 << 5) | b5);

                destPtr[0] = (byte)(rgb565 & 0xFF);       // LSB
                destPtr[1] = (byte)((rgb565 >> 8) & 0xFF); // MSB

                srcPtr += 4;   // BGRA8888 → 4 Bytes weiter
                destPtr += 2;  // RGB565 → 2 Bytes weiter
            }
        }

        return output;
    }

    /// <summary>
    /// Draws a key in the "center" display area based on the given index.
    /// </summary>
    private async Task DrawKey(int index, SKBitmap bitmap)
    {
        if (index < 0 || index >= Columns * Rows)
            throw new Exception($"Key {index} is not a valid key");

        // Example dimension values from the old code
        const int keyWidth = 90;
        const int keyHeight = 90;

        if (VisibleX == null || Columns == 0)
            throw new Exception("VisibleX or Columns is not set");

        // Calculate position
        var x = VisibleX[0] + (index % Columns) * keyWidth;
        var y = (index / Columns) * keyHeight;

        // Call the DrawCanvas method
        await DrawCanvas("center", keyWidth, keyHeight, bitmap, x, y);
    }

    /// <summary>
    /// Draws a touch button on the corresponding key, optionally with an image and text overlay.
    /// </summary>
    public async Task DrawTouchButton(
        TouchButton touchButton,
        LoupedeckConfig config,
        bool refresh,
        int columns)
    {
        ArgumentNullException.ThrowIfNull(touchButton);

        if (refresh || touchButton.RenderedImage == null)
        {
            var renderedBitmap =
                BitmapHelper.RenderTouchButtonContent(touchButton, config, 90, 90, columns);
            if (renderedBitmap == null) return;
        }

        try
        {
            await DrawKey(touchButton.Index, touchButton.RenderedImage);
        }
        catch (TimeoutException ex)
        {
            // Device not Responding
            Console.WriteLine($"Timeout occurred: {ex.Message}");
        }
        catch (Exception ex)
        {
            // Other unexpected errors
            Console.WriteLine($"Unexpected error: {ex.Message}");
        }
    }

    public async Task DrawTextButton(int index, string text)
    {
        if (string.IsNullOrEmpty(text))
            throw new ArgumentException("Text must not be null or empty.", nameof(text));

        var renderedBitmap = BitmapHelper.RenderTextToBitmap(text, 90, 90);
        if (renderedBitmap == null)
            throw new Exception("The rendering of the text has failed.");

        try
        {
            await DrawKey(index, renderedBitmap);
        }
        catch (TimeoutException ex)
        {
            // Device not Responding
            Console.WriteLine($"Timeout occurred: {ex.Message}");
        }
        catch (Exception ex)
        {
            // Other unexpected errors
            Console.WriteLine($"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    /// Draws the entire screen (display) identified by the given ID.
    /// </summary>
    public async Task DrawScreen(string id, SKBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        await DrawCanvas(id, 0, 0, bitmap);
    }

    /// <summary>
    /// Triggers a refresh (redraw) of the display.
    /// </summary>
    private async Task Refresh(string id)
    {
        if (Displays == null || !Displays.TryGetValue(id, out var displayInfo))
            throw new Exception($"Display '{id}' is not available on this device!");

        await SendAsync(Constants.Command.DRAW, displayInfo.Id);
    }

    /// <summary>
    /// Retrieves device information (SERIAL and VERSION).
    /// </summary>
    public (byte[] serial, string version) GetInfo()
    {
        if (_connection == null || !_connection.IsReady)
            throw new Exception("Not connected!");

        var serialResponse = Send(Constants.Command.SERIAL);
        var versionResponse = Send(Constants.Command.VERSION);
        var version = $"{versionResponse[0]}.{versionResponse[1]}.{versionResponse[2]}";

        return (serialResponse, version);
    }

    /// <summary>
    /// Sets the brightness level of the device.
    /// </summary>
    public async Task SetBrightness(double value)
    {
        var byteValue = (int)Math.Clamp(
            Math.Round(value * Constants.MaxBrightness),
            0,
            Constants.MaxBrightness
        );

        await SendAsync(Constants.Command.SET_BRIGHTNESS, [(byte)byteValue]);
    }

    /// <summary>
    /// Sets the color of a button by its ID.
    /// </summary>
    public async Task SetButtonColor(Constants.ButtonType id, Color color)
    {
        byte key = 0;
        var found = false;

        foreach (var kv in Constants.Buttons)
        {
            if (kv.Value != id) continue;

            key = kv.Key;
            found = true;
            break;
        }

        if (!found)
            throw new Exception($"Invalid button ID: {id}");

        var r = color.R;
        var g = color.G;
        var b = color.B;
        var data = new[] { key, r, g, b };

        await SendAsync(Constants.Command.SET_COLOR, data);
    }

    /// <summary>
    /// Triggers a haptic vibration.
    /// </summary>
    public void Vibrate(byte pattern = Constants.VibrationPattern.Short)
    {
        SendNoResponse(Constants.Command.SET_VIBRATION, [pattern]);
    }

    /// <summary>
    /// Performs a device reset.
    /// </summary>
    public void ResetDevice()
    {
        SendNoResponse(Constants.Command.RESET);
    }
}