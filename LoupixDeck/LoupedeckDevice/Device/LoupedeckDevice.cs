using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Threading.Channels;
using Avalonia.Media;
using LoupixDeck.LoupedeckDevice.Serial;
using LoupixDeck.Models;
using LoupixDeck.Utils;
using SkiaSharp;

namespace LoupixDeck.LoupedeckDevice.Device;

/// <summary>
/// Base class for Loupedeck devices.
/// Contains all functionalities (connection, sending/receiving, button, rotation, and touch events, drawing, etc.).
/// </summary>
public abstract class LoupedeckDevice
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
    private bool _queueWorkerStarted;
    private volatile bool _suppressAutoReconnect;

    // Accessed concurrently from the send-queue worker thread (insert) and the
    // serial read thread (lookup/remove). Plain Dictionary corrupts under that
    // race ("non-concurrent collection" / "Index was outside the bounds of the
    // array"), so these must be concurrent collections.
    private readonly ConcurrentDictionary<byte, TaskCompletionSource<byte[]>> _pendingTransactions = new();
    private readonly ConcurrentDictionary<byte, CancellationTokenSource> _pendingTimeouts = new();
    private readonly Dictionary<byte, TouchInfo> _touches = new();

#nullable enable

    private int ReconnectInterval { get; }
    public string? Host { get; set; }
    private string? Path { get; }
    private int Baudrate { get; }

    protected abstract ImmutableDictionary<string, DisplayInfo>? Displays { get; }
    public abstract int[] Buttons { get; }
    public abstract int Columns { get; }
    public abstract int Rows { get; }
    protected virtual int[]? VisibleX { get; }
    protected virtual int[]? VisibleY { get; }
    public abstract string Type { get; }
    public abstract string ProductId { get; }

    /// <summary>Number of rotary encoders (knobs) the device exposes. Subclasses must set this.</summary>
    public abstract int RotaryCount { get; }

    /// <summary>
    /// True when the device has the two narrow side display strips next to the dial
    /// columns (Razer Stream Controller). Gates side-strip-only behaviour — independent
    /// left/right rotary paging, swipe-to-page, full-height strip rendering — so devices
    /// without strips (Live S) are untouched. Base returns false; Razer overrides.
    /// </summary>
    public virtual bool HasSideStrips => false;

    /// <summary>
    /// X-offset (in panel/wallpaper pixels) at which the centre touch grid starts on
    /// the unified panel. Devices with side strips reserve the leftmost strip width
    /// (Razer: 60), so the page wallpaper maps to its true panel position and stays
    /// continuous with the strips across the bezel. 0 (default) for full-width grids.
    /// </summary>
    public virtual int WallpaperGridXOffset => 0;

    /// <summary>
    /// Returns the touch slot that physically sits next to the rotary at
    /// <paramref name="rotaryIndex"/>, or -1 when the device has no such
    /// neighbour. Plugins use this for transient feedback overlays (e.g. a
    /// volume read-out flashed on the slot beside the rotary the user just
    /// turned). Subclasses override per device geometry; the base returns -1.
    /// </summary>
    public virtual int GetTouchSlotForRotary(int rotaryIndex) => -1;

    /// <summary>
    /// Number of addressable touch buttons. Defaults to Columns*Rows; devices with
    /// extra non-grid touch slots (e.g. Razer side panels) override this in their ctor.
    /// </summary>
    public abstract int TouchButtonCount { get; }
#nullable restore

    public event EventHandler<ConnectionEventArgs> OnConnect;
    public event EventHandler<ConnectionEventArgs> OnDisconnect;
    public event EventHandler<ButtonEventArgs> OnButton; // "down" or "up"
    public event EventHandler<RotateEventArgs> OnRotate;
    public event EventHandler<TouchEventArgs> OnTouch;

    /// <summary>
    /// Fired for touches on the Loupedeck CT's centre wheel screen (command bytes
    /// WHEEL_TOUCH/WHEEL_TOUCH_END, confirmed via hardware trace) — kept separate
    /// from <see cref="OnTouch"/> because the wheel's coordinate space (0-240) and
    /// touch-id namespace are independent of the main grid's, and the wheel has no
    /// dedicated "click" signal: a press shows up as a tight cluster of touch
    /// start/end events near the screen's centre, which a consumer can detect from
    /// this event's coordinates rather than from a button code.
    /// </summary>
    public event EventHandler<TouchEventArgs> OnWheelTouch;

    /// <summary>
    /// Fired when a vertical swipe is detected on a side strip. Only devices with
    /// <see cref="HasSideStrips"/> raise this; consumers page the matching dial column.
    /// </summary>
    public event EventHandler<SwipeEventArgs> OnSwipe;

#nullable enable

    /// <summary>
    /// Initializes a new instance of the <see cref="LoupedeckDevice"/> class.
    /// </summary>
    /// <param name="host">Host name or IP (if applicable).</param>
    /// <param name="path">Device path (e.g. serial port).</param>
    /// <param name="baudrate">Device Connection Baudrate</param>
    /// <param name="autoConnect">If true, attempts to connect automatically.</param>
    /// <param name="reconnectInterval">Interval (ms) to wait before reconnecting.</param>
    protected LoupedeckDevice(string? host = null, string? path = null, int baudrate = 0, bool autoConnect = true,
        int reconnectInterval = Constants.DefaultReconnectInterval)
    {
        Host = host;
        Path = path;
        ReconnectInterval = reconnectInterval;
        Baudrate = baudrate > 0 ? baudrate : 115200;
        if (autoConnect)
            ConnectBlind();
    }

#nullable restore

    protected static int[] InitSimpleArray(int length) => Enumerable.Range(0, length).ToArray();

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
            if (_suppressAutoReconnect) return;
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
        // Reconnect() reuses the same send channel, so we must not spin up a
        // second worker each time Connect() runs.
        if (_queueWorkerStarted) return;
        _queueWorkerStarted = true;

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
                        if (SendDiagnostics.Enabled) SendDiagnostics.OnSent(_transactionId);
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
        _suppressAutoReconnect = true;
        _sendChannel.Writer.TryComplete();
        _connection?.Close();
    }

    /// <summary>
    /// Tears down the current serial connection and re-establishes it on the
    /// same Device instance, so external event subscribers (OnButton, OnTouch,
    /// OnRotate, …) remain wired up. The auto-reconnect handler is suppressed
    /// while we close, and the port is briefly opened by a probe before the
    /// real connect — that DTR pulse is what gets the device into a workable
    /// state on the very first connection (mirrors InitSetup.TestConnection).
    /// </summary>
    public void Reconnect()
    {
        _suppressAutoReconnect = true;
        try
        {
            _connection?.Close();
        }
        catch
        {
            // ignored — best-effort tear-down
        }
        _connection = null;

        // Give the OS time to release the COM port; without this, the next
        // SerialPort.Open() throws UnauthorizedAccessException on Windows.
        Thread.Sleep(500);

        try
        {
            ProbeWake();
        }
        catch
        {
            // ignored — Connect() will surface the real error
        }

        _suppressAutoReconnect = false;
        ConnectBlind();
    }

    /// <summary>
    /// Opens and immediately closes the serial port to pulse DTR/RTS and put
    /// the device into a state where the handshake can succeed.
    /// </summary>
    private void ProbeWake()
    {
        if (string.IsNullOrEmpty(Path)) return;

        using var probe = new System.IO.Ports.SerialPort(Path, Baudrate)
        {
            ReadTimeout = 500,
            WriteTimeout = 500
        };
        probe.Open();
        Thread.Sleep(150);
        probe.Close();
        // Let the device finish its USB-CDC re-enumeration before the real open.
        Thread.Sleep(300);
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
    /// Time to wait for a DRAW (refresh) ACK before giving up. The device answers in
    /// ~0ms when it answers at all, but occasionally omits the ACK entirely (issue #149)
    /// while still performing the refresh. A short timeout bounds the wait.
    /// </summary>
    private static readonly TimeSpan DrawAckTimeout = TimeSpan.FromMilliseconds(750);

    /// <summary>
    /// Queues a DRAW (refresh) command on its own path, separate from <see cref="SendAsync"/>.
    /// A missing response is NOT an error here: the device sometimes skips the DRAW ACK
    /// even though it performed the refresh (issue #149), so on timeout the task completes
    /// successfully instead of throwing — no exception, no spurious log, and the render
    /// path is not stalled for the full default timeout. A real ACK, when it arrives,
    /// still completes the task via OnReceive exactly like any other command.
    /// </summary>
    private async Task SendDrawAsync(byte[] data)
    {
        data ??= [];

        var timeoutCts = new CancellationTokenSource(DrawAckTimeout);
        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

        timeoutCts.Token.Register(() =>
        {
            // Benign: complete successfully on a missing ACK. TrySetResult returns false
            // if a real response already won via OnReceive, so this stays a no-op then.
            if (tcs.TrySetResult([]) && SendDiagnostics.Enabled)
                SendDiagnostics.OnTimeout(Constants.Command.DRAW, benign: true);
        });

        var item = new QueueItem(Constants.Command.DRAW, data, tcs, true, timeoutCts);

        // The cancellation token is not used for the write operation:
        // ReSharper disable once MethodSupportsCancellation
        await _sendChannel.Writer.WriteAsync(item);

        await tcs.Task;
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
    // Set LOUPIXDECK_DEBUG_PROTOCOL=1 to log raw incoming frames (button/rotate
    // byte codes, touch coordinates, and any command byte not in Constants.Command)
    // straight to the console. Used to map an unfamiliar device's protocol (e.g.
    // the Loupedeck CT's named buttons and wheel) by pressing each control and
    // reading the byte off here, instead of needing the vendor's own software.
    private static readonly bool DebugProtocol =
        Environment.GetEnvironmentVariable("LOUPIXDECK_DEBUG_PROTOCOL") == "1";

    private void OnReceive(byte[] buff)
    {
        if (buff.Length < 3) return;

        var msgLength = buff[0];
        var command = buff[1];
        var transactionId = buff[2];
        var payload = buff.Skip(3).Take(msgLength - 3).ToArray();

        if (DebugProtocol && !Enum.IsDefined(typeof(Constants.Command), command))
        {
            Console.WriteLine($"[Protocol] Unrecognized command 0x{command:x2} payload=[{string.Join(",", payload.Select(b => b.ToString("x2")))}]");
        }

        var matched = _pendingTransactions.TryRemove(transactionId, out var transaction);
        if (matched)
        {
            // TrySetResult: a timeout may have already completed this TCS with an
            // exception, in which case SetResult would throw.
            transaction.TrySetResult(payload);
        }

        if (SendDiagnostics.Enabled) SendDiagnostics.OnReceived(transactionId, command, matched);

        // Additionally, cancel the timeout if it exists:
        if (_pendingTimeouts.TryRemove(transactionId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
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
            case (byte)Constants.Command.WHEEL_TOUCH:
                OnWheelTouchReceived(Constants.TouchEventType.TOUCH_START, payload);
                break;
            case (byte)Constants.Command.WHEEL_TOUCH_END:
                OnWheelTouchReceived(Constants.TouchEventType.TOUCH_END, payload);
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
        var evt = (buff[1] == 0x00) ? Constants.ButtonEventType.BUTTON_DOWN : Constants.ButtonEventType.BUTTON_UP;

        if (!Constants.Buttons.TryGetValue(btn, out var id))
        {
            if (DebugProtocol)
                Console.WriteLine($"[Protocol] BUTTON_PRESS unmapped byte 0x{btn:x2} ({evt})");
            return;
        }

        if (DebugProtocol)
            Console.WriteLine($"[Protocol] BUTTON_PRESS byte 0x{btn:x2} -> {id} ({evt})");

        OnButton?.Invoke(this, new ButtonEventArgs { ButtonId = id, EventType = evt });
    }

    /// <summary>
    /// Handles incoming rotation (knob) data.
    /// </summary>
    private void OnRotateReceived(byte[] buff)
    {
        if (buff.Length < 2) return;
        var btn = buff[0];
        var delta = (sbyte)buff[1];

        if (!Constants.Buttons.TryGetValue(btn, out var id))
        {
            if (DebugProtocol)
                Console.WriteLine($"[Protocol] KNOB_ROTATE unmapped byte 0x{btn:x2} delta={delta}");
            return;
        }

        if (DebugProtocol)
            Console.WriteLine($"[Protocol] KNOB_ROTATE byte 0x{btn:x2} -> {id} delta={delta}");

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

        if (DebugProtocol)
            Console.WriteLine($"[Protocol] {eventType} x={x} y={y} touchId={touchId} raw0=0x{buff[0]:x2}");

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

            // Side-strip swipe: compare this end point against where the finger went
            // down. A dominant vertical move on a strip pages that dial column; the
            // OnTouch consumer ignores strip slots, so taps and swipes don't collide.
            if (HasSideStrips && _touchStarts.TryGetValue(touchId, out var start))
                DetectSideStripSwipe(start, x, y);
            _touchStarts.Remove(touchId);
        }
        else
        {
            if (!_touches.ContainsKey(touchId))
            {
                eventType = Constants.TouchEventType.TOUCH_START;
                if (HasSideStrips)
                    _touchStarts[touchId] = (x, y);
            }
            _touches[touchId] = touch;
        }

        OnTouch?.Invoke(this, new TouchEventArgs
        {
            EventType = eventType,
            Touches = _touches.Values.ToList(),
            ChangedTouch = touch
        });
    }

    // Separate from _touches: the wheel screen has its own touch-id namespace and
    // coordinate space (0-240), so mixing it into the grid's dictionary could let a
    // wheel touch and a grid touch with the same firmware-assigned id clobber each
    // other.
    private readonly Dictionary<byte, TouchInfo> _wheelTouches = new();

    /// <summary>
    /// Handles touches on the CT centre wheel's own screen. Coordinates are local
    /// to that 240x240 display, so this deliberately does NOT call <see cref="GetTarget"/>
    /// (which maps the main grid's 0-360x270 space) — Target is a fixed "knob"
    /// marker with no slot index. Click-vs-drag gesture detection is left to the
    /// consumer (see <see cref="OnWheelTouch"/> doc).
    /// </summary>
    private void OnWheelTouchReceived(Constants.TouchEventType eventType, byte[] buff)
    {
        if (buff.Length < 6) return;
        var x = (buff[1] << 8) | buff[2];
        var y = (buff[3] << 8) | buff[4];
        var touchId = buff[5];

        if (DebugProtocol)
            Console.WriteLine($"[Protocol] WHEEL {eventType} x={x} y={y} touchId={touchId} raw0=0x{buff[0]:x2}");

        var touch = new TouchInfo
        {
            X = x,
            Y = y,
            Id = touchId,
            Target = new TouchTarget { Screen = "knob", Key = -1 }
        };

        if (eventType == Constants.TouchEventType.TOUCH_END)
        {
            _wheelTouches.Remove(touchId);
        }
        else
        {
            if (!_wheelTouches.ContainsKey(touchId))
                eventType = Constants.TouchEventType.TOUCH_START;
            _wheelTouches[touchId] = touch;
        }

        OnWheelTouch?.Invoke(this, new TouchEventArgs
        {
            EventType = eventType,
            Touches = _wheelTouches.Values.ToList(),
            ChangedTouch = touch
        });
    }

    // Per-finger down position, kept only while HasSideStrips, to classify the
    // release as a tap vs. a vertical swipe on a side strip.
    private readonly Dictionary<byte, (int X, int Y)> _touchStarts = new();

    // A swipe must move at least this far vertically and dominate the horizontal
    // movement; below this, the gesture is treated as a tap (no paging).
    private const int SwipeMinVertical = 30;

    /// <summary>
    /// Classifies a finger release that started and ended on the same side strip as
    /// an up/down swipe and raises <see cref="OnSwipe"/>. Only the strip x-ranges
    /// (left &lt; VisibleX[0], right ≥ VisibleX[1]) qualify, so the centre grid is
    /// never affected.
    /// </summary>
    private void DetectSideStripSwipe(in (int X, int Y) start, int endX, int endY)
    {
        if (VisibleX == null) return;

        SideStrip? StripOf(int x) =>
            x < VisibleX[0] ? SideStrip.Left :
            x >= VisibleX[1] ? SideStrip.Right :
            null;

        var startStrip = StripOf(start.X);
        // Require the gesture to stay on the same strip it began on.
        if (startStrip == null || startStrip != StripOf(endX))
            return;

        var dy = endY - start.Y;
        var dx = endX - start.X;
        if (Math.Abs(dy) < SwipeMinVertical || Math.Abs(dy) <= Math.Abs(dx))
            return;

        OnSwipe?.Invoke(this, new SwipeEventArgs
        {
            Side = startStrip.Value,
            Direction = dy < 0 ? SwipeDirection.Up : SwipeDirection.Down
        });
    }

    /// <summary>
    /// This method is overridden in derived classes to determine which area or key is touched.
    /// </summary>
    protected virtual TouchTarget GetTarget(int x, int y) => new() { Screen = "center", Key = -1 };

    /// <summary>
    /// Sends a 16-bit (5-6-5) image buffer to display "id" at the position (x,y).
    /// </summary>
    private async Task DrawBuffer(string id, int width, int height, byte[] buffer, int x = 0, int y = 0,
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

        header[0] = (byte)((x >> 8) & 0xff);
        header[1] = (byte)(x & 0xff);
        header[2] = (byte)((y >> 8) & 0xff);
        header[3] = (byte)(y & 0xff);
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
    protected async Task DrawCanvas(
        string id,
        int width,
        int height,
        SKBitmap bitmap,
        int x = 0,
        int y = 0,
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
        if (bitmap == null || bitmap.IsNull)
            throw new InvalidOperationException("Bitmap ist null oder leer.");

        if (bitmap.ColorType != SKColorType.Bgra8888)
            throw new InvalidOperationException("Bitmap muss im Format BGRA8888 vorliegen.");

        int width = bitmap.Width;
        int height = bitmap.Height;
        int pixelCount = width * height;

        // Output array for 16-bit RGB565 (2 bytes per pixel)
        byte[] output = new byte[pixelCount * 2];

        // Pixel access runs under the shared render gate so it never overlaps a
        // RenderTouchButtonContent/composite on another thread
        // (see SkiaRenderGate / docs/CRASH_ANALYSIS_ACCESS_VIOLATION.md, measure 1).
        lock (SkiaRenderGate.Sync)
        {
            // Access the pixel data as a pointer. PeekPixels returns null for an
            // inaccessible/empty bitmap; without a null check the pointer access
            // below would end in an AccessViolation instead of a catchable exception.
            using SKPixmap pixmap = bitmap.PeekPixels(); // Fast access without a copy
            if (pixmap == null)
                throw new InvalidOperationException("Bitmap pixel data is not accessible (PeekPixels returned null).");

            byte* srcPtr = (byte*)pixmap.GetPixels().ToPointer();
            if (srcPtr == null)
                throw new InvalidOperationException("Bitmap pixel pointer is null.");

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

                    srcPtr += 4;   // advance 4 bytes (BGRA8888)
                    destPtr += 2;  // advance 2 bytes (RGB565)
                }
            }

            // Ensures the bitmap (owner of the native pixel buffer that srcPtr points
            // to) is not finalized during the loop → no use-after-free.
            GC.KeepAlive(bitmap);
        }

        return output;
    }

    /// <summary>
    /// Draws a key in the "center" display area based on the given index.
    /// </summary>
    private async Task DrawKey(int index, SKBitmap bitmap, bool autoRefresh = true)
    {
        if (index < 0 || index >= Columns * Rows)
            throw new Exception($"Key {index} is not a valid key");

        // Example dimension values from the old code
        const int keyWidth = 90;
        const int keyHeight = 90;

        if (VisibleX == null || Columns == 0)
            throw new Exception("VisibleX or Columns is not set");

        // Calculate position
        var x = VisibleX[0] + ((index % Columns) * keyWidth);
        var y = (index / Columns) * keyHeight;

        // Call the DrawCanvas method
        await DrawCanvas("center", keyWidth, keyHeight, bitmap, x, y, autoRefresh);
    }

    /// <summary>
    /// Draws a touch button on the corresponding key, optionally with an image and text overlay.
    /// </summary>
    public virtual async Task DrawTouchButton(
        TouchButton touchButton,
        LoupedeckConfig config,
        bool refresh,
        int columns)
    {
        ArgumentNullException.ThrowIfNull(touchButton);

        if (refresh || touchButton.RenderedImage == null)
        {
            var renderedBitmap =
                BitmapHelper.RenderTouchButtonContent(touchButton, config, 90, 90, columns, WallpaperGridXOffset);
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

    /// <summary>
    /// Draws an arbitrary bitmap directly to the touch slot at the given index, bypassing
    /// the per-button render cache. Used by the folder-navigation overlay so that the
    /// configured TouchButton state is not mutated.
    /// </summary>
    public virtual async Task DrawTouchSlot(int index, SKBitmap bitmap, bool refresh = true)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        try
        {
            await DrawKey(index, bitmap, refresh);
        }
        catch (TimeoutException ex)
        {
            Console.WriteLine($"Timeout occurred: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    /// Draws all center-grid touch slots in one shot: composites the per-slot
    /// bitmaps into a single full-display image and issues ONE framebuffer write
    /// plus ONE refresh. Drawing slots individually instead triggers a full-display
    /// refresh per slot, so the screen visibly rebuilds slot-by-slot (tearing).
    /// This is the path proven by the video-streaming PoC. <paramref name="slotBitmaps"/>
    /// is indexed by slot number; null entries keep the cleared background.
    /// </summary>
    public virtual async Task DrawTouchSlotsAtomic(IReadOnlyList<SKBitmap> slotBitmaps, bool refresh = true)
    {
        if (slotBitmaps == null || slotBitmaps.Count == 0) return;
        if (Displays == null || !Displays.TryGetValue("center", out var center)) return;

        var xBase = VisibleX is { Length: > 0 } ? VisibleX[0] : 0;
        const int keySize = 90;

        using var full = new SKBitmap(new SKImageInfo(center.Width, center.Height,
            SKColorType.Bgra8888, SKAlphaType.Premul));

        // Composit the per-slot bitmaps under the shared render gate so this draw can
        // never overlap a per-button render/convert on another thread (see
        // SkiaRenderGate / docs/CRASH_ANALYSIS_ACCESS_VIOLATION.md, measure 1). The
        // lock covers only the synchronous Skia work — the device I/O below is awaited
        // outside it.
        lock (SkiaRenderGate.Sync)
        {
            using var canvas = new SKCanvas(full);
            canvas.Clear(SKColors.Black);
            for (var slot = 0; slot < slotBitmaps.Count && slot < Columns * Rows; slot++)
            {
                var bmp = slotBitmaps[slot];
                if (bmp == null) continue;
                var x = xBase + ((slot % Columns) * keySize);
                var y = (slot / Columns) * keySize;
                canvas.DrawBitmap(bmp, x, y);
            }
        }

        try
        {
            await DrawScreen("center", full, refresh);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DrawTouchSlotsAtomic failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Exposes the unified-display canvas for subclasses that draw non-grid
    /// regions (e.g. the Razer side panels at x=0 / x=420).
    /// </summary>
    protected Task DrawCanvasRegion(string displayId, int width, int height, SKBitmap bitmap, int x, int y,
        bool autoRefresh = true)
        => DrawCanvas(displayId, width, height, bitmap, x, y, autoRefresh);

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
    /// Pixel size of the named display. Used by diagnostics/benchmarks to build a
    /// correctly-sized full-screen bitmap for <see cref="DrawScreen"/>. Returns
    /// (0,0) when the display is unknown.
    /// </summary>
    public (int Width, int Height) GetDisplaySize(string id = "center")
        => Displays != null && Displays.TryGetValue(id, out var d) ? (d.Width, d.Height) : (0, 0);

    /// <summary>
    /// Draws the entire screen (display) identified by the given ID.
    /// </summary>
    public async Task DrawScreen(string id, SKBitmap bitmap, bool refresh = true)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        await DrawCanvas(id, 0, 0, bitmap, autoRefresh: refresh);
    }

    /// <summary>
    /// Triggers a refresh (redraw) of the display.
    /// </summary>
    private async Task Refresh(string id)
    {
        if (Displays == null || !Displays.TryGetValue(id, out var displayInfo))
            throw new Exception($"Display '{id}' is not available on this device!");

        // DRAW uses its own path: a missing ACK is tolerated (see SendDrawAsync / #149).
        await SendDrawAsync(displayInfo.Id);
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
    /// Per-button native haptic slot (DRV2605 effect, scheduled relative to touch-start).
    /// </summary>
    public readonly record struct HapticSlot(byte ButtonId, byte Sequence, byte EffectId, byte DelayMs, byte DurationMs);

    /// <summary>
    /// Enables firmware-side haptic feedback for touch buttons.
    /// Reverse-engineered op-code 0x2e — payload: [screen, 0x00, count, (btn, seq, fx, delay, dur) * count].
    /// </summary>
    public void EnableNativeHaptic(IReadOnlyList<HapticSlot> slots, byte screen = 0x4d)
    {
        if (slots == null || slots.Count == 0)
            throw new ArgumentOutOfRangeException(nameof(slots));

        var data = new byte[3 + (slots.Count * 5)];
        data[0] = screen;
        data[1] = 0x00;
        data[2] = (byte)slots.Count;
        var i = 3;
        foreach (var s in slots)
        {
            data[i++] = s.ButtonId;
            data[i++] = s.Sequence;
            data[i++] = s.EffectId;
            data[i++] = s.DelayMs;
            data[i++] = s.DurationMs;
        }

        SendNoResponse(Constants.Command.SET_HAPTIC, data);
    }

    /// <summary>
    /// Disables firmware-side haptic feedback (op-code 0x2e, payload [screen, 0x01]).
    /// </summary>
    public void DisableNativeHaptic(byte screen = 0x4d)
    {
        SendNoResponse(Constants.Command.SET_HAPTIC, [screen, 0x01]);
    }

    /// <summary>
    /// Sets the global haptic strength (0x00 = off … 0x04 = strongest).
    /// Op-code 0x19, payload [0x02, 0x03, 0x00, 0x0a, strength].
    /// </summary>
    public void SetHapticStrength(byte strength)
    {
        if (strength > 0x04)
            throw new ArgumentOutOfRangeException(nameof(strength), "Strength must be 0x00..0x04.");

        SendNoResponse(Constants.Command.SET_HAPTIC_STRENGTH, [0x02, 0x03, 0x00, 0x0a, strength]);
    }

    /// <summary>
    /// Performs a device reset.
    /// </summary>
    public void ResetDevice()
    {
        SendNoResponse(Constants.Command.RESET);
    }
}