using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using LoupixDeck.LoupedeckDevice.Serial;
using LoupixDeck.Models;

namespace LoupixDeck.LoupedeckDevice.Device;

/// <summary>
/// Base class for Loupedeck devices.
/// Contains all functionalities (connection, sending/receiving, button, rotation, and touch events, drawing, etc.).
/// </summary>
public class LoupedeckDevice
{
    private ISerialConnection _connection;
    private byte _transactionId;

    private readonly Dictionary<byte, TaskCompletionSource<byte[]>> _pendingTransactions = new();
    private readonly Dictionary<byte, TouchInfo> _touches = new();

    private int ReconnectInterval { get; set; }
    public string Host { get; set; }
    private string Path { get; set; }

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
    /// <param name="autoConnect">If true, attempts to connect automatically.</param>
    /// <param name="reconnectInterval">Interval (ms) to wait before reconnecting.</param>
    protected LoupedeckDevice(string host = null, string path = null, bool autoConnect = true,
        int reconnectInterval = Constants.DefaultReconnectInterval)
    {
        Host = host;
        Path = path;
        ReconnectInterval = reconnectInterval;
        if (autoConnect)
        {
            ConnectBlind();
        }
    }

    /// <summary>
    /// Searches (synchronously) for available devices.
    /// </summary>
    private static List<DiscoveredDevice> ListDevices(bool ignoreSerial = false)
    {
        var devices = new List<DiscoveredDevice>();
        if (ignoreSerial) return devices;
        var devicesSerial = SerialConnection.DiscoverPorts();

        foreach (var port in devicesSerial)
        {
            if (port.Contains("ttyACM"))
            {
                devices.Add(new DiscoveredDevice
                {
                    ConnectionType = typeof(SerialConnection),
                    Path = port
                });
            }
        }

        return devices;
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
            _connection = new SerialConnection(Path);
        }
        else
        {
            var devices = ListDevices();
            if (devices.Count > 0)
            {
                var device = devices[0];
                if (device.Path != null)
                {
                    _connection = new SerialConnection(device.Path);
                }
                else
                {
                    OnDisconnect?.Invoke(this, new ConnectionEventArgs("N/A", new Exception("Device path is null")));
                    return;
                }
            }

            if (_connection == null)
            {
                OnDisconnect?.Invoke(this, new ConnectionEventArgs("N/A", new Exception("No device found")));
                return;
            }
        }

        _connection.Connected += (s, e) => OnConnect?.Invoke(this, e);
        _connection.MessageReceived += (s, e) => OnReceive(e.Data);
        _connection.Disconnected += (s, e) =>
        {
            OnDisconnect?.Invoke(this, e);
            Thread.Sleep(ReconnectInterval);
            ConnectBlind();
        };

        _connection.Connect();
    }

    /// <summary>
    /// Closes the current connection.
    /// </summary>
    public void Close() => _connection?.Close();

    /// <summary>
    /// Sends a command with the given data and waits synchronously for the response.
    /// Frame format: [length (1 byte), command (1 byte), transactionID (1 byte), data]
    /// </summary>
    private byte[] Send(Constants.Command command, byte[] data = null)
    {
        data ??= Array.Empty<byte>();

        _transactionId = (byte)((_transactionId + 1) % 256);
        if (_transactionId == 0)
            _transactionId++;

        var length = (byte)Math.Min(3 + data.Length, 0xff);
        byte[] header = { length, (byte)command, _transactionId };
        var packet = header.Concat(data).ToArray();
        var tcs = new TaskCompletionSource<byte[]>();

        _pendingTransactions[_transactionId] = tcs;
        _connection?.Send(packet);

        return tcs.Task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Sends a command with the given data but does not wait for a response.
    /// </summary>
    private void SendNoResponse(Constants.Command command, byte[] data = null)
    {
        data ??= Array.Empty<byte>();

        _transactionId = (byte)((_transactionId + 1) % 256);
        if (_transactionId == 0)
            _transactionId++;

        var length = (byte)Math.Min(3 + data.Length, 0xff);
        byte[] header = { length, (byte)command, _transactionId };
        var packet = header.Concat(data).ToArray();

        _connection?.Send(packet);
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

        if (!_pendingTransactions.TryGetValue(transactionId, out var transaction)) return;

        transaction.SetResult(payload);
        _pendingTransactions.Remove(transactionId);
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
            if (_touches.ContainsKey(touchId))
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
    private void DrawBuffer(string id, int width, int height, byte[] buffer, int? x = 0, int? y = 0,
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
        Send(Constants.Command.FRAMEBUFF, data);

        if (autoRefresh)
            Refresh(id);
    }

    /// <summary>
    /// Creates a drawing surface with the correct dimensions, executes the callback function for drawing,
    /// and sends the resulting buffer to the device.
    /// </summary>
    /// <param name="id">Display ID.</param>
    /// <param name="width">Width (0 = use the display's default width).</param>
    /// <param name="height">Height (0 = use the display's default height).</param>
    /// <param name="drawAction">Callback with the DrawingContext, width, and height.</param>
    /// <param name="x">X-position in the header.</param>
    /// <param name="y">Y-position in the header.</param>
    /// <param name="autoRefresh">Should a refresh be triggered automatically?</param>
    private void DrawCanvas(
        string id,
        int width,
        int height,
        Action<DrawingContext, int, int> drawAction,
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

        // 1) Create a RenderTargetBitmap (Avalonia drawing surface)
        var rtb = new RenderTargetBitmap(
            new PixelSize(width, height),
            new Vector(96, 96) // Typical DPI
        );

        // 2) Execute the drawing operation
        using (var ctx = rtb.CreateDrawingContext(true))
        {
            drawAction(ctx, width, height);
        }

        // 3) Convert the resulting RenderTargetBitmap into a 16-bit-5-6-5 array
        var buffer = ConvertRtbToRaw16Bpp(rtb);

        // 4) Pass the buffer to the actual DrawBuffer
        DrawBuffer(id, width, height, buffer, x, y, autoRefresh);
    }

    /// <summary>
    /// Converts a RenderTargetBitmap (usually BGRA32) into a 16-bit-565 byte array.
    /// </summary>
    private byte[] ConvertRtbToRaw16Bpp(RenderTargetBitmap rtb)
    {
        var pixelWidth = rtb.PixelSize.Width;
        var pixelHeight = rtb.PixelSize.Height;
        var stride = pixelWidth * 4; // BGRA32 = 4 bytes per pixel
        var bufferSize = stride * pixelHeight;

        // 1) Allocate a byte array to receive the BGRA data
        var bgraBytes = new byte[bufferSize];

        // 2) Pin it and get an IntPtr
        var handle =
            System.Runtime.InteropServices.GCHandle.Alloc(bgraBytes,
                System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            var ptr = handle.AddrOfPinnedObject();

            // 3) CopyPixels copies the bitmap data (BGRA) into bgraBytes
            rtb.CopyPixels(
                sourceRect: new PixelRect(0, 0, pixelWidth, pixelHeight),
                buffer: ptr,
                bufferSize: bufferSize,
                stride: stride
            );
        }
        finally
        {
            handle.Free();
        }

        // 4) Output array in 16-bit-565 format: 2 bytes per pixel
        var output = new byte[pixelWidth * pixelHeight * 2];
        int outIndex = 0;

        // BGRA32 => RGB565 conversion (Little Endian)
        for (int i = 0; i < bufferSize; i += 4)
        {
            byte b = bgraBytes[i + 0];
            byte g = bgraBytes[i + 1];
            byte r = bgraBytes[i + 2];
            // byte a = bgraBytes[i + 3]; // If you need alpha

            int r5 = (r * 31) / 255; // 0..31
            int g6 = (g * 63) / 255; // 0..63
            int b5 = (b * 31) / 255; // 0..31

            ushort rgb565 = (ushort)((r5 << 11) | (g6 << 5) | b5);

            // Little-Endian: LSB, then MSB
            output[outIndex++] = (byte)(rgb565 & 0xFF);
            output[outIndex++] = (byte)(rgb565 >> 8);
        }

        return output;
    }

    /// <summary>
    /// Draws a key in the "center" display area based on the given index.
    /// </summary>
    private void DrawKey(int index, Action<DrawingContext, int, int> drawAction)
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
        DrawCanvas("center", keyWidth, keyHeight, drawAction, x, y);
    }

    /// <summary>
    /// Draws text at the given position in the specified DrawingContext.
    /// </summary>
    private void DrawTextAt(
        DrawingContext context,
        string text,
        Color color,
        double textSize,
        bool centered,
        double posX = 0,
        double posY = 0,
        double imageWidth = 90,
        double imageHeight = 90,
        bool bold = false,
        bool italic = false,
        bool outlined = false,
        Color outlineColor = default)
    {
        if (context == null || string.IsNullOrEmpty(text))
            throw new ArgumentException("The drawing context or text must not be null!");

        var brush = new ImmutableSolidColorBrush(color);

        // Create typeface with bold or Italic
        var typeface = new Typeface(
            FontFamily.Default,
            italic ? FontStyle.Italic : FontStyle.Normal,
            bold ? FontWeight.Bold : FontWeight.Normal
        );

        // Create the FormattedText (Avalonia 11+)
        var formattedText = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface, // Default font
            textSize,
            brush
        )
        {
            TextAlignment = TextAlignment.Left,
            MaxTextHeight = 85,
            MaxTextWidth = 85
        };

        var textWidth = formattedText.Width;
        var textHeight = formattedText.Height;

        int drawX;
        int drawY;

        if (centered)
        {
            var centerX = imageWidth / 2;
            var centerY = imageHeight / 2;

            drawX = (int)Math.Round(centerX - (textWidth / 2));
            drawY = (int)Math.Round(centerY - (textHeight / 2));
        }
        else
        {
            drawX = (int)Math.Round(posX);
            drawY = (int)Math.Round(posY);
        }

        // Falls eine Umrandung gewünscht ist, wird sie gezeichnet
        if (outlined)
        {
            var outlineBrush = new ImmutableSolidColorBrush(outlineColor);
            const int outlineOffset = 1; // Stärke der Umrandung
            
            var outlineText = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface, // Default font
                textSize,
                outlineBrush
            )
            {
                TextAlignment = TextAlignment.Left,
                MaxTextHeight = 85,
                MaxTextWidth = 85
            };

            // Zeichne den Umrandungstext in mehreren Richtungen
            context.DrawText(outlineText, new Point(drawX - outlineOffset, drawY)); // Links
            context.DrawText(outlineText, new Point(drawX + outlineOffset, drawY)); // Rechts
            context.DrawText(outlineText, new Point(drawX, drawY - outlineOffset)); // Oben
            context.DrawText(outlineText, new Point(drawX, drawY + outlineOffset)); // Unten
            context.DrawText(outlineText, new Point(drawX - outlineOffset, drawY - outlineOffset)); // Links-oben
            context.DrawText(outlineText, new Point(drawX + outlineOffset, drawY - outlineOffset)); // Rechts-oben
            context.DrawText(outlineText, new Point(drawX - outlineOffset, drawY + outlineOffset)); // Links-unten
            context.DrawText(outlineText, new Point(drawX + outlineOffset, drawY + outlineOffset)); // Rechts-unten
        }

        context.DrawText(formattedText, new Point(drawX, drawY));
    }

    /// <summary>
    /// Draws a touch button on the corresponding key, optionally with an image and text overlay.
    /// </summary>
    /// <param name="touchButton">The TouchButton object containing index, bitmap, text, etc.</param>
    public void DrawTouchButton(TouchButton touchButton)
    {
        ArgumentNullException.ThrowIfNull(touchButton);

        DrawKey(touchButton.Index, (context, width, height) =>
        {
            var renderedBitmap = RenderTouchButtonContent(touchButton, width, height);
            if (renderedBitmap == null) return;

            var srcRect = new Rect(
                0, 0,
                renderedBitmap.PixelSize.Width,
                renderedBitmap.PixelSize.Height
            );
            var dstRect = new Rect(0, 0, width, height);

            context.DrawImage(renderedBitmap, srcRect, dstRect);
        });
    }

    /// <summary>
    /// Renders the content of a TouchButton (background, image, text) into an Avalonia bitmap.
    /// </summary>
    public Bitmap RenderTouchButtonContent(TouchButton touchButton, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(touchButton);

        var rtb = new RenderTargetBitmap(
            new PixelSize(width, height),
            new Vector(96, 96) // DPI
        );

        using var ctx = rtb.CreateDrawingContext(true);

        var backgroundBrush = new ImmutableSolidColorBrush(touchButton.BackColor);
        ctx.DrawRectangle(
            backgroundBrush,
            pen: null,
            rect: new Rect(0, 0, width, height)
        );

        if (touchButton.Image != null)
        {
            var imageWidth = touchButton.Image.PixelSize.Width;
            var imageHeight = touchButton.Image.PixelSize.Height;

            var sourceRect = new Rect(0, 0, imageWidth, imageHeight);

            // Calculate the ratio to scale the image to the target size
            var scaleX = width / (double)imageWidth;
            var scaleY = height / (double)imageHeight;

            // Select the smaller ratio to fit the image completely without distortion
            var baseScale = Math.Min(scaleX, scaleY);

            // Applying scaling using ImageScale
            var scaleFactor = Math.Max(0.01, touchButton.ImageScale / 100.0);
            var finalScale = baseScale * scaleFactor;

            // New width and height after scaling (aspect ratio is retained)
            var scaledWidth = imageWidth * finalScale;
            var scaledHeight = imageHeight * finalScale;

            // Centre the image if it is smaller than the target image
            var posX = touchButton.ImagePositionX;
            var posY = touchButton.ImagePositionY;

            if (posX == 0 && posY == 0) // Standard: Set image in the centre if no position is specified
            {
                posX = (int)((width - scaledWidth) / 2);
                posY = (int)((height - scaledHeight) / 2);
            }

            var destRect = new Rect(posX, posY, scaledWidth, scaledHeight);

            ctx.DrawImage(touchButton.Image, sourceRect, destRect);
        }

        if (!string.IsNullOrEmpty(touchButton.Text))
        {
            DrawTextAt(
                ctx,
                touchButton.Text,
                touchButton.TextColor,
                touchButton.TextSize,
                touchButton.TextCentered,
                touchButton.TextPositionX,
                touchButton.TextPositionY,
                width,
                height,
                touchButton.Bold,
                touchButton.Italic,
                touchButton.Outlined,
                touchButton.OutlineColor
            );
        }

        touchButton.RenderedImage = rtb;
        return rtb;
    }

    /// <summary>
    /// Draws the entire screen (display) identified by the given ID.
    /// </summary>
    public void DrawScreen(string id, Action<DrawingContext, int, int> drawAction)
    {
        DrawCanvas(id, 0, 0, drawAction);
    }

    /// <summary>
    /// Triggers a refresh (redraw) of the display.
    /// </summary>
    private void Refresh(string id)
    {
        if (Displays == null || !Displays.TryGetValue(id, out var displayInfo))
            throw new Exception($"Display '{id}' is not available on this device!");

        Send(Constants.Command.DRAW, displayInfo.Id);
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
    public void SetBrightness(double value)
    {
        var byteValue = (int)Math.Clamp(
            Math.Round(value * Constants.MaxBrightness),
            0,
            Constants.MaxBrightness
        );

        Send(Constants.Command.SET_BRIGHTNESS, [(byte)byteValue]);
    }

    /// <summary>
    /// Sets the color of a button by its ID.
    /// </summary>
    public void SetButtonColor(Constants.ButtonType id, Color color)
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

        Send(Constants.Command.SET_COLOR, data);
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