using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Threading.Channels;
using Avalonia.Media;
using LoupixDeck.LoupedeckDevice.Serial;
using LoupixDeck.Models;
using LoupixDeck.Registry;
using LoupixDeck.Utils;
using SkiaSharp;

namespace LoupixDeck.LoupedeckDevice.Device;

/// <summary>
/// Base class for Loupedeck devices.
/// Contains all functionalities (connection, sending/receiving, button, rotation, and touch events, drawing, etc.).
/// </summary>
public class LoupedeckDevice
{
    /// <summary>
    /// Pixel geometry and capabilities of this device model. Every key- and panel-sized
    /// drawing operation reads its dimensions from here instead of a constant, so a device
    /// with a different tile or panel size renders pixel-exact without any scaling step.
    /// Base is the Loupedeck family's 90px key on a 480x270 panel; subclasses override.
    /// </summary>
    public virtual DeviceGeometry Geometry => DeviceGeometry.Default;

    /// <summary>
    /// Where this device's keys sit on its panel. Defaults to the model's measured layout
    /// and is replaced by the user's own measurement when their config carries one — the
    /// controller pushes it here on load and whenever it changes, so this stays the one
    /// place the draw path asks.
    /// </summary>
    public KeyGridCalibration KeyCalibration
    {
        get => field ?? Geometry.DefaultKeyCalibration;
        set;
    }

    /// <summary>
    /// Edge length of one centre-grid touch key in device pixels — the calibrated size, so
    /// every renderer that sizes a tile from it produces pixels the panel takes unscaled.
    /// </summary>
    public int KeySize => KeyCalibration.KeySize;

    /// <summary>
    /// The rectangle key <paramref name="index"/> occupies in the "center" framebuffer.
    /// The single source of truth for key positions: with a gapped grid the old
    /// <c>index % Columns * KeySize</c> is simply wrong, and it was spelled out in half a
    /// dozen places.
    /// </summary>
    /// <remarks>
    /// The x origin is <see cref="GridOriginX"/> — <c>VisibleX[0]</c> on a unified panel
    /// buffer, 0 on the CT whose "center" is a grid-only buffer. The y origin is 0 on every
    /// device, including those whose <c>VisibleY[0]</c> is not (the Live S bezel inset is
    /// horizontal only as far as the framebuffer is concerned).
    /// </remarks>
    public SKRectI GetKeyRect(int index) => KeyRectFrom(index, GridOriginX, 0);

    /// <summary>
    /// Centre of key <paramref name="index"/> in touch coordinates, which are panel-wide
    /// and therefore measured from <c>VisibleX/VisibleY</c> rather than from the
    /// framebuffer origin.
    /// </summary>
    public SKPointI GetKeyTouchCenter(int index)
    {
        int xBase = VisibleX is { Length: > 0 } ? VisibleX[0] : 0;
        int yBase = VisibleY is { Length: > 0 } ? VisibleY[0] : 0;
        SKRectI rect = KeyRectFrom(index, xBase, yBase);
        return new SKPointI(rect.MidX, rect.MidY);
    }

    /// <summary>
    /// The rectangle key <paramref name="index"/> samples from a panel-wide wallpaper. Same
    /// grid, different origin: the wallpaper spans the whole panel, so the grid sits at
    /// <see cref="WallpaperGridXOffset"/> in it rather than at the framebuffer's origin
    /// (they differ on the CT, whose "center" is a grid-only buffer).
    /// </summary>
    public SKRectI GetWallpaperKeyRect(int index) => KeyRectFrom(index, WallpaperGridXOffset, 0);

    /// <summary>
    /// Size of the bitmap that covers the whole key grid, as
    /// <see cref="DrawCenterGridRegion"/> expects it: the union of every key rectangle,
    /// measured from the grid origin and clipped to the "center" buffer. On a gapless grid
    /// this is the familiar <c>Columns * KeySize</c>; on a gapped one the outer keys can
    /// hang over the edge of the glass, and the part that hangs over is not part of the
    /// region — it is the same clip the per-key path applies.
    /// </summary>
    public SKSizeI GetGridRegionSize()
    {
        SKRectI last = GetKeyRect((Columns * Rows) - 1);
        int originX = GridOriginX;

        int width = last.Right - originX;
        int height = last.Bottom;

        if (Displays != null && Displays.TryGetValue("center", out DisplayInfo center))
        {
            width = Math.Min(width, center.Width - originX);
            height = Math.Min(height, center.Height);
        }

        // A grid whose first key starts left of / above the origin is clipped there: the
        // region begins at the origin rather than growing to include the overhang.
        return new SKSizeI(Math.Max(0, width), Math.Max(0, height));
    }

    /// <summary>
    /// Key <paramref name="index"/>'s rectangle within the grid-region bitmap
    /// <see cref="GetGridRegionSize"/> describes — the same rectangle as
    /// <see cref="GetKeyRect"/>, shifted to that bitmap's own origin.
    /// </summary>
    public SKRectI GetKeyRectInGridRegion(int index)
    {
        SKRectI rect = GetKeyRect(index);
        rect.Offset(-GridOriginX, 0);
        return rect;
    }

    private SKRectI KeyRectFrom(int index, int xBase, int yBase)
    {
        int columns = Columns > 0 ? Columns : 1;
        KeyGridCalibration calibration = KeyCalibration;
        (int x, int y) = calibration.GetKeyOrigin(index % columns, index / columns);
        return SKRectI.Create(xBase + x, yBase + y, calibration.KeySize, calibration.KeySize);
    }

    /// <summary>
    /// Bits per channel the panel actually resolves, which is not always what the RGB565
    /// wire format encodes. Drives the dither target grid in
    /// <see cref="ConvertSKBitmapToRaw16BppUnsafe"/>. Base is the full RGB565 depth, which
    /// the Loupedeck Live S was measured to resolve; devices that resolve less override.
    /// </summary>
    public virtual (int Red, int Green, int Blue) PanelChannelBits => (5, 6, 5);

    /// <summary>
    /// Applies ordered dithering when downsampling to the panel's framebuffer, turning the
    /// hard steps of a gradient into a pattern the eye averages back into intermediate tones.
    /// Colours already sitting on the panel grid are unaffected.
    ///
    /// Driven by <see cref="Models.LoupedeckConfig.DitheringEnabled"/>: the controller applies it
    /// on start-up and whenever the setting changes. Defaults to off, matching that setting's
    /// default, so a device that draws before any config has been applied behaves as it always did.
    /// </summary>
    public bool DitherFramebuffer { get; set; }

    private byte[] _ditherLutRed;
    private byte[] _ditherLutGreen;
    private byte[] _ditherLutBlue;

    private ISerialConnection _connection;

    /// <summary>
    /// Serializes every connect attempt. Without it the auto-reconnect loop and an explicit
    /// <see cref="Reconnect"/> (Settings button / system resume, issue #195) can open the
    /// port at the same time — the second one fails with "port already open" and drops the
    /// working connection object.
    /// </summary>
    private readonly Lock _connectGate = new();

    private byte _transactionId;

    private readonly record struct QueueItem(
        Constants.Command Command,
        byte[] Data,
        int Offset,
        int Length,
        TaskCompletionSource<byte[]> Completion,
        bool ExpectResponse,
        CancellationTokenSource TimeoutCts,
        bool ReturnDataToPool,
        bool HasReservedWsPrefix);

    private readonly Channel<QueueItem> _sendChannel = Channel.CreateUnbounded<QueueItem>();

    // Reused after TryReset(). A cancelled CTS (real or DRAW timeout) cannot be reset and is disposed.
    private static readonly ConcurrentBag<CancellationTokenSource> TimeoutCtsPool = [];
    private static readonly CancellationTokenSource FireAndForgetCts = new();
    private bool _queueWorkerStarted;
    private volatile bool _suppressAutoReconnect;

    // 0 = no reconnect pending, 1 = one scheduled. Guards against spawning
    // parallel reconnect chains: the Disconnected event can fire more than once
    // for a single failed connection (the Send() error path fires it, then the
    // following Close() fires it again).
    private int _reconnectPending;

    // Accessed concurrently from the send-queue worker thread (insert) and the
    // serial read thread (lookup/remove). Plain Dictionary corrupts under that
    // race ("non-concurrent collection" / "Index was outside the bounds of the
    // array"), so these must be concurrent collections.
    private readonly ConcurrentDictionary<byte, TaskCompletionSource<byte[]>> _pendingTransactions = new();
    private readonly ConcurrentDictionary<byte, CancellationTokenSource> _pendingTimeouts = new();
    private readonly Dictionary<byte, TouchInfo> _touches = new();

    private int ReconnectInterval { get; set; }
    public string Host { get; set; }
    private string Path { get; set; }
    private int Baudrate { get; set; }

    protected FrozenDictionary<string, DisplayInfo> Displays { get; init; } =
        FrozenDictionary<string, DisplayInfo>.Empty;
    public int[] Buttons { get; set; }
    public int Columns { get; protected init; }
    public int Rows { get; protected init; }
    protected int[] VisibleX { get; init; }
    protected int[] VisibleY { get; init; }
    public string Type { get; set; }
    public string ProductId { get; set; }

    /// <summary>Number of rotary encoders (knobs) the device exposes. Subclasses must set this.</summary>
    public int RotaryCount { get; protected init; }

    /// <summary>
    /// True when the device has the two narrow side display strips next to the dial
    /// columns (Razer Stream Controller). Gates side-strip-only behaviour — independent
    /// left/right rotary paging, swipe-to-page, full-height strip rendering — so devices
    /// without strips (Live S) are untouched. Base returns false; Razer overrides.
    /// </summary>
    public virtual bool HasSideStrips => false;

    /// <summary>
    /// Maps a raw BUTTON_PRESS byte to a centre-grid slot on devices whose grid is physical
    /// keys instead of a touchscreen. Base never matches, so every other device resolves its
    /// button bytes through <see cref="Constants.Buttons"/> exactly as before.
    /// </summary>
    protected virtual bool TryGetPhysicalKeySlot(byte raw, out int slot)
    {
        slot = -1;
        return false;
    }

    /// <summary>
    /// X-offset (in panel/wallpaper pixels) at which the centre touch grid starts on
    /// the unified panel. Devices with side strips reserve the leftmost strip width
    /// (Razer: 60), so the page wallpaper maps to its true panel position and stays
    /// continuous with the strips across the bezel. 0 (default) for full-width grids.
    /// </summary>
    public virtual int WallpaperGridXOffset => 0;

    /// <summary>
    /// X-origin of the centre touch grid inside the "center" framebuffer. Devices with a
    /// unified panel buffer place the grid at <c>VisibleX[0]</c> (Razer: 60, Live S: 15,
    /// Stream Controller X: 0); the CT, whose "center" is a dedicated grid-only buffer,
    /// overrides this to 0. Mirrors the origin <see cref="DrawTouchSlotsAtomic"/> and
    /// <see cref="DrawCenterGridRegion"/> compute inline; named here so diagnostics can draw
    /// against the same one. Framebuffer space only — touch coordinates use VisibleX directly.
    /// </summary>
    public virtual int GridOriginX => VisibleX is { Length: > 0 } ? VisibleX[0] : 0;

    /// <summary>
    /// Largest RGB565 payload the panel accepts in a single FRAMEBUFF write, or 0 for no
    /// limit. A device that has one does not report the failure: it acknowledges the write
    /// like any other and then shows nothing, so an oversized frame is indistinguishable
    /// from a working one until someone looks at the glass. <see cref="DrawCanvas"/> splits
    /// anything larger into horizontal bands and refreshes once at the end.
    /// </summary>
    public virtual int MaxFramebufferPayloadBytes => 0;

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
    public int TouchButtonCount { get; protected init; }

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
        // 0 means "not configured" — the caller normally passes the model's rate from the
        // device registry, so this fallback only covers a device constructed outside that
        // path. It used to be a literal 115200, which no entry in the registry states.
        Baudrate = baudrate > 0 ? baudrate : Constants.DefaultBaudrate;
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
        lock (_connectGate)
        {
            // Another attempt won the race and is already connected.
            if (_connection is { IsReady: true }) return;

            try
            {
                Connect();
            }
            catch
            {
                // Errors are reported in the Disconnect event
            }
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
            ScheduleReconnect();
        };

        _connection.Connect();

        StartQueueWorker();
    }

    /// <summary>
    /// Schedules a single auto-reconnect attempt on a fresh background thread.
    /// Crucially this does NOT retry inline: the Disconnected event is raised
    /// synchronously from inside <see cref="SerialConnection.Connect"/>'s failure
    /// path, so retrying on the firing thread would recurse (Connect → fail →
    /// Disconnected → ConnectBlind → Connect → …) ~3 s per level and never return.
    /// On the very first connect that recursion never lets the ctor finish, so
    /// DeviceService.StartDevice blocks forever on _deviceCreatedEvent and the app
    /// hangs at the splash with an empty log (issue #146). Off-loading to its own
    /// thread lets the initial Connect() return and turns reconnect into a flat,
    /// throttled loop (each attempt spawns the next, then dies — stack stays shallow).
    /// </summary>
    private void ScheduleReconnect()
    {
        // Only ever one reconnect attempt in flight at a time.
        if (Interlocked.CompareExchange(ref _reconnectPending, 1, 0) != 0)
            return;

        Thread thread = new(() =>
        {
            try
            {
                Thread.Sleep(ReconnectInterval);
            }
            finally
            {
                // Release the slot before retrying so the next failure (raised
                // synchronously from within ConnectBlind below) can schedule the
                // following attempt instead of being silently dropped.
                Interlocked.Exchange(ref _reconnectPending, 0);
            }

            if (_suppressAutoReconnect) return;
            ConnectBlind();
        })
        {
            IsBackground = true,
            Name = "LoupedeckReconnect"
        };
        thread.Start();
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

                    int packetLength = 3 + item.Length;
                    byte lengthByte = (byte)Math.Min(packetLength, 0xff);

                    if (item.ExpectResponse)
                    {
                        // A previous command on this 1-byte id may have timed out without an
                        // ACK; reclaim its CTS so the pool does not leak across the wrap.
                        if (_pendingTimeouts.TryRemove(_transactionId, out CancellationTokenSource staleCts))
                            ReturnTimeoutCts(staleCts);

                        _pendingTransactions[_transactionId] = item.Completion;
                        _pendingTimeouts[_transactionId] = item.TimeoutCts;
                        if (SendDiagnostics.Enabled) SendDiagnostics.OnSent(_transactionId);
                    }

                    if (item.HasReservedWsPrefix)
                    {
                        item.Data[item.Offset] = lengthByte;
                        item.Data[item.Offset + 1] = (byte)item.Command;
                        item.Data[item.Offset + 2] = _transactionId;
                        _connection?.SendMaskedInPlace(item.Data, item.Offset, packetLength);
                    }
                    else
                    {
                        SendSmallPacket(item, lengthByte);
                    }

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
                finally
                {
                    if (item.ReturnDataToPool)
                        ArrayPool<byte>.Shared.Return(item.Data);
                }
            }
        });
    }

    /// <summary>
    /// True while the serial link is open and usable. Lets callers tell a successful
    /// <see cref="Reconnect"/> from one that silently failed (issue #195).
    /// </summary>
    public bool IsConnected => _connection is { IsReady: true };

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
        // Held across the whole tear-down + re-open so a pending auto-reconnect attempt
        // can't grab the port in between (the lock is re-entered by ConnectBlind below).
        lock (_connectGate)
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
    private Task<byte[]> SendAsync(Constants.Command command, byte[] data = null)
    {
        data ??= [];
        return EnqueueAsync(
            command,
            data,
            offset: 0,
            length: data.Length,
            expectResponse: true,
            tolerateMissingAck: false,
            timeout: TimeSpan.FromSeconds(3),
            returnDataToPool: false,
            hasReservedWsPrefix: false);
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
    private Task SendDrawAsync(byte[] data)
    {
        data ??= [];
        return EnqueueAsync(
            Constants.Command.DRAW,
            data,
            offset: 0,
            length: data.Length,
            expectResponse: true,
            tolerateMissingAck: true,
            timeout: DrawAckTimeout,
            returnDataToPool: false,
            hasReservedWsPrefix: false);
    }

    /// <summary>
    /// Queues a command and optional data to be sent to the device without waiting for a response.
    /// Used for fire-and-forget operations where no reply is expected.
    /// </summary>
    /// <param name="command">The command to send to the device.</param>
    /// <param name="data">Optional payload data for the command.</param>
    /// <returns>A task that completes when the command has been sent.</returns>
    private Task SendNoResponseAsync(Constants.Command command, byte[] data = null)
    {
        data ??= [];
        return EnqueueAsync(
            command,
            data,
            offset: 0,
            length: data.Length,
            expectResponse: false,
            tolerateMissingAck: false,
            timeout: null,
            returnDataToPool: false,
            hasReservedWsPrefix: false);
    }

    private async Task<byte[]> EnqueueAsync(
        Constants.Command command,
        byte[] data,
        int offset,
        int length,
        bool expectResponse,
        bool tolerateMissingAck,
        TimeSpan? timeout,
        bool returnDataToPool,
        bool hasReservedWsPrefix)
    {
        TaskCompletionSource<byte[]> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenSource timeoutCts = expectResponse
            ? RentTimeoutCts(timeout!.Value)
            : FireAndForgetCts;

        if (expectResponse)
        {
            timeoutCts.Token.Register(() =>
            {
                if (tolerateMissingAck)
                {
                    // Benign: complete successfully on a missing ACK. TrySetResult returns false
                    // if a real response already won via OnReceive, so this stays a no-op then.
                    if (tcs.TrySetResult([]) && SendDiagnostics.Enabled)
                        SendDiagnostics.OnTimeout(command, benign: true);
                }
                else
                {
                    tcs.TrySetException(new TimeoutException($"Timeout waiting for response to command {command}."));
                }
            });
        }

        QueueItem item = new(
            command,
            data,
            offset,
            length,
            tcs,
            expectResponse,
            timeoutCts,
            returnDataToPool,
            hasReservedWsPrefix);

        try
        {
            // The cancellation token is not used for the write operation:
            // ReSharper disable once MethodSupportsCancellation
            await _sendChannel.Writer.WriteAsync(item);
        }
        catch
        {
            if (returnDataToPool)
                ArrayPool<byte>.Shared.Return(data);
            if (expectResponse)
                ReturnTimeoutCts(timeoutCts);
            throw;
        }

        return await tcs.Task;
    }

    private void SendSmallPacket(in QueueItem item, byte lengthByte)
    {
        int packetLength = 3 + item.Length;
        Span<byte> packet = packetLength <= 256
            ? stackalloc byte[packetLength]
            : new byte[packetLength];

        packet[0] = lengthByte;
        packet[1] = (byte)item.Command;
        packet[2] = _transactionId;
        if (item.Length > 0)
            item.Data.AsSpan(item.Offset, item.Length).CopyTo(packet.Slice(3));

        _connection?.Send(packet);
    }

    private static CancellationTokenSource RentTimeoutCts(TimeSpan timeout)
    {
        if (TimeoutCtsPool.TryTake(out CancellationTokenSource cts))
        {
            if (cts.TryReset())
            {
                cts.CancelAfter(timeout);
                return cts;
            }

            cts.Dispose();
        }

        return new CancellationTokenSource(timeout);
    }

    private static void ReturnTimeoutCts(CancellationTokenSource cts)
    {
        if (ReferenceEquals(cts, FireAndForgetCts))
            return;

        if (cts.TryReset())
            TimeoutCtsPool.Add(cts);
        else
            cts.Dispose();
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

        if (_pendingTimeouts.TryRemove(transactionId, out CancellationTokenSource cts))
            ReturnTimeoutCts(cts);

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

        // A physical-key grid reports its keys here rather than as TOUCH frames. Translate
        // them into synthetic touches instead of emitting a simple-button event: such a
        // device has no LED buttons, so a button event would have no config entry to match.
        if (TryGetPhysicalKeySlot(btn, out var keySlot))
        {
            if (DebugProtocol)
                Console.WriteLine($"[Protocol] BUTTON_PRESS byte 0x{btn:x2} -> physical key slot {keySlot} ({evt})");

            EmitSyntheticKeyTouch(keySlot, evt);
            return;
        }

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
    /// Synthetic touch ids start above any contact id the firmware assigns, so an emulated
    /// key press can never collide with a real finger on a device that has both.
    /// </summary>
    private const byte SyntheticTouchIdBase = 0x80;

    /// <summary>
    /// Feeds a physical key press into the normal touch path as a touch at the centre of that
    /// key. Re-entering <see cref="OnTouchReceived"/> rather than raising OnTouch directly is
    /// what keeps the live-contact bookkeeping (and therefore press-and-hold) correct and the
    /// event shape identical to a real touch, so no consumer needs to know the difference.
    /// </summary>
    private void EmitSyntheticKeyTouch(int slot, Constants.ButtonEventType evt)
    {
        // The centre of the key's calibrated rectangle, so the synthetic touch lands on
        // the same key the renderer drew there.
        SKPointI centre = GetKeyTouchCenter(slot);
        int x = centre.X;
        int y = centre.Y;

        byte[] buff =
        [
            0,
            (byte)(x >> 8), (byte)(x & 0xff),
            (byte)(y >> 8), (byte)(y & 0xff),
            (byte)(SyntheticTouchIdBase + slot)
        ];

        OnTouchReceived(
            evt == Constants.ButtonEventType.BUTTON_DOWN
                ? Constants.TouchEventType.TOUCH_START
                : Constants.TouchEventType.TOUCH_END,
            buff);
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
    /// <paramref name="packet"/> is a rented buffer whose layout is
    /// [WS header slot][3-byte command header][display id][x,y,w,h][pixels].
    /// Ownership of the rental transfers to the send queue.
    /// </summary>
    private async Task DrawBuffer(
        string id,
        DisplayInfo displayInfo,
        int width,
        int height,
        byte[] packet,
        int packetOffset,
        int packetLength,
        bool autoRefresh)
    {
        try
        {
            int pixelBytes = width * height * 2;
            int expectedPacket = 3 + displayInfo.Id.Length + 8 + pixelBytes;
            if (packetLength != expectedPacket)
                throw new Exception($"Expected buffer length of {pixelBytes}, got {packetLength - 3 - displayInfo.Id.Length - 8}!");

            Task send = EnqueueAsync(
                Constants.Command.FRAMEBUFF,
                packet,
                packetOffset,
                packetLength - 3,
                expectResponse: true,
                tolerateMissingAck: false,
                timeout: TimeSpan.FromSeconds(3),
                returnDataToPool: true,
                hasReservedWsPrefix: true);
            packet = null;

            await send;

            if (autoRefresh)
                await Refresh(id);
        }
        finally
        {
            if (packet != null)
                ArrayPool<byte>.Shared.Return(packet);
        }
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
        ArgumentNullException.ThrowIfNull(bitmap);

        // Determine the display
        if (Displays == null || !Displays.TryGetValue(id, out var displayInfo))
            throw new Exception($"Display '{id}' is not available on this device!");

        // If width/height = 0 => use the display's default values
        if (width == 0)
            width = displayInfo.Width;
        if (height == 0)
            height = displayInfo.Height;

        // The bitmap must cover the declared region exactly: the conversion below sizes its
        // source from the BITMAP while the FRAMEBUFF header describes the REGION, so any
        // mismatch produces a buffer the device reads under the wrong geometry. The
        // destination length check in ConvertSKBitmapToRaw16BppUnsafe catches the case where
        // the total area differs, but a transposed region (a 90x96 bitmap into a 96x90 key)
        // has the same byte count and passes that check, landing on the panel as a sheared
        // image. Fail here instead, where the display, the position and both sizes are still
        // known — a key-sized bitmap built for the wrong device geometry (90px tile on the
        // 96px Stream Controller X) is then named outright.
        if (bitmap.Width != width || bitmap.Height != height)
            throw new ArgumentException(
                $"Bitmap is {bitmap.Width}x{bitmap.Height} but the region on display '{id}' at " +
                $"({x},{y}) is {width}x{height}.", nameof(bitmap));

        // Clip the region to the panel. A key grid does not have to tile its display: a
        // device whose keys are spaced wider than they are large puts the outer ones partly
        // past the glass, so the region legitimately hangs over an edge. The guard above has
        // already done its job on the region as asked for, which is what keeps a wrong-sized
        // tile an error rather than something silently trimmed to fit here.
        //
        // Clipping moves the read window instead of copying the bitmap — the converter takes
        // the source rectangle, so an edge draw costs no allocation.
        int clipLeft = Math.Max(0, -x);
        int clipTop = Math.Max(0, -y);
        int clipRight = Math.Max(0, (x + width) - displayInfo.Width);
        int clipBottom = Math.Max(0, (y + height) - displayInfo.Height);

        int drawX = x + clipLeft;
        int drawY = y + clipTop;
        int drawWidth = width - clipLeft - clipRight;
        int drawHeight = height - clipTop - clipBottom;

        // Entirely off the panel: nothing to send, and a zero-sized FRAMEBUFF would be a
        // malformed packet rather than a no-op.
        if (drawWidth <= 0 || drawHeight <= 0)
            return;

        // A panel that caps its FRAMEBUFF payload takes the frame in horizontal bands. The
        // rows of a band are contiguous in the buffer, so each one is a normal region write
        // at the same x and its own y — no reassembly on the device side. Only the last band
        // refreshes, so the frame still appears in one go rather than banding into view.
        int rowBytes = drawWidth * 2;
        int bandRows = drawHeight;
        int maxPayload = MaxFramebufferPayloadBytes;
        if (maxPayload > 0 && rowBytes > 0 && (long)rowBytes * drawHeight > maxPayload)
            bandRows = Math.Max(1, maxPayload / rowBytes);

        for (int top = 0; top < drawHeight; top += bandRows)
        {
            int rows = Math.Min(bandRows, drawHeight - top);
            bool isLast = top + rows >= drawHeight;
            SKRectI src = SKRectI.Create(clipLeft, clipTop + top, drawWidth, rows);
            await DrawCanvasBand(id, displayInfo, bitmap, src, drawX, drawY + top,
                autoRefresh && isLast);
        }
    }

    /// <summary>
    /// Sends one horizontal band of a canvas: builds the FRAMEBUFF packet for the
    /// <paramref name="srcRect"/> window of the bitmap and addresses it at
    /// (<paramref name="x"/>, <paramref name="y"/>) on the display.
    /// </summary>
    private async Task DrawCanvasBand(
        string id,
        DisplayInfo displayInfo,
        SKBitmap bitmap,
        SKRectI srcRect,
        int x,
        int y,
        bool autoRefresh)
    {
        int width = srcRect.Width;
        int rows = srcRect.Height;

        // One rented buffer holds the WS prefix, command header, display id, x/y/w/h,
        // and RGB565 pixels. The send queue writes the command header and masks in place.
        byte[] displayId = displayInfo.Id;
        int pixelBytes = width * rows * 2;
        int commandPacketLength = 3 + displayId.Length + 8 + pixelBytes;
        int wsHeaderLength = SerialConnection.MaskedHeaderLength(commandPacketLength);
        int packetOffset = wsHeaderLength;
        int bodyOffset = packetOffset + 3;

        byte[] rented = ArrayPool<byte>.Shared.Rent(wsHeaderLength + commandPacketLength);
        try
        {
            displayId.CopyTo(rented.AsSpan(bodyOffset));

            Span<byte> header = rented.AsSpan(bodyOffset + displayId.Length, 8);
            BinaryPrimitives.WriteUInt16BigEndian(header, (ushort)x);
            BinaryPrimitives.WriteUInt16BigEndian(header.Slice(2), (ushort)y);
            BinaryPrimitives.WriteUInt16BigEndian(header.Slice(4), (ushort)width);
            BinaryPrimitives.WriteUInt16BigEndian(header.Slice(6), (ushort)rows);

            ConvertSKBitmapToRaw16BppUnsafe(
                bitmap,
                rented.AsSpan(bodyOffset + displayId.Length + 8, pixelBytes),
                x,
                y,
                srcRect);

            byte[] packet = rented;
            rented = null;
            await DrawBuffer(id, displayInfo, width, rows, packet, packetOffset, commandPacketLength, autoRefresh);
        }
        finally
        {
            if (rented != null)
                ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Resolves (and caches) the per-channel dither tables for this device's panel depth.
    /// The tables themselves are shared process-wide per (wireBits, panelBits) pair, so two
    /// devices of the same kind do not build them twice.
    /// </summary>
    private (byte[] Red, byte[] Green, byte[] Blue) GetDitherLuts()
    {
        if (_ditherLutRed == null)
        {
            (int red, int green, int blue) = PanelChannelBits;
            _ditherLutRed = Rgb565Dither.GetLut(5, red);
            _ditherLutGreen = Rgb565Dither.GetLut(6, green);
            _ditherLutBlue = Rgb565Dither.GetLut(5, blue);
        }

        return (_ditherLutRed, _ditherLutGreen, _ditherLutBlue);
    }

    /// <summary>
    /// Converts a RenderTargetBitmap (usually BGRA32) into 16-bit-565 bytes in <paramref name="output"/>.
    /// </summary>
    /// <param name="bitmap">Source bitmap in BGRA8888.</param>
    /// <param name="output">Destination RGB565 bytes; must be at least width*height*2 long.
    /// A rented array may be larger than that — only the exact pixel count is written.</param>
    /// <param name="originX">Absolute X of the converted window on the target display.</param>
    /// <param name="originY">Absolute Y of the converted window on the target display. For a band
    /// this is the band's own Y, not the bitmap's, so the dither pattern keeps anchoring to
    /// absolute display coordinates and bands do not seam.</param>
    /// <param name="srcRect">Window of the bitmap to convert. Reading a sub-rectangle is how
    /// both banding and edge clipping avoid copying the bitmap: the read window moves instead.</param>
    private unsafe void ConvertSKBitmapToRaw16BppUnsafe(SKBitmap bitmap, Span<byte> output, int originX,
        int originY, SKRectI srcRect)
    {
        if (bitmap == null || bitmap.IsNull)
            throw new InvalidOperationException("Bitmap is null or empty.");

        if (bitmap.ColorType != SKColorType.Bgra8888)
            throw new InvalidOperationException("Bitmap must be BGRA8888.");

        if (srcRect.Left < 0 || srcRect.Top < 0 || srcRect.Width < 0 || srcRect.Height < 0 ||
            srcRect.Right > bitmap.Width || srcRect.Bottom > bitmap.Height)
            throw new ArgumentOutOfRangeException(nameof(srcRect), srcRect,
                $"Source window lies outside the {bitmap.Width}x{bitmap.Height} bitmap.");

        int srcLeft = srcRect.Left;
        int srcTop = srcRect.Top;
        int width = srcRect.Width;
        int height = srcRect.Height;

        int pixelCount = width * height;
        int pixelBytes = pixelCount * 2;
        if (output.Length < pixelBytes)
            throw new ArgumentException($"RGB565 destination is {output.Length} bytes, need {pixelBytes}.", nameof(output));

        output = output.Slice(0, pixelBytes);

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

            byte* srcBase = (byte*)pixmap.GetPixels().ToPointer();
            if (srcBase == null)
                throw new InvalidOperationException("Bitmap pixel pointer is null.");

            // Rows are not necessarily contiguous — honour the pixmap's stride.
            int srcRowBytes = pixmap.RowBytes;

            bool dither = DitherFramebuffer;
            byte[] lutR = null, lutG = null, lutB = null;
            if (dither)
            {
                (lutR, lutG, lutB) = GetDitherLuts();
            }

            fixed (byte* destPtrFixed = output)
            fixed (byte* lutRFixed = lutR, lutGFixed = lutG, lutBFixed = lutB)
            {
                byte* destPtr = destPtrFixed;

                for (int row = 0; row < height; row++)
                {
                    byte* srcPtr = srcBase + ((long)(srcTop + row) * srcRowBytes) + ((long)srcLeft * 4);
                    for (int col = 0; col < width; col++)
                    {
                        byte b = srcPtr[0];
                        byte g = srcPtr[1];
                        byte r = srcPtr[2];
                        // byte a = srcPtr[3]; // optional

                        int r5, g6, b5;
                        if (dither)
                        {
                            // Dither against the panel's real depth, not against RGB565's.
                            int t = Rgb565Dither.ThresholdAt(originX + col, originY + row);
                            r5 = lutRFixed[(r * Rgb565Dither.Thresholds) + t];
                            g6 = lutGFixed[(g * Rgb565Dither.Thresholds) + t];
                            b5 = lutBFixed[(b * Rgb565Dither.Thresholds) + t];
                        }
                        else
                        {
                            // RGB888 → RGB565, rounding to the nearest level. Plain truncation
                            // biases every channel towards black by up to one level (8/255 on
                            // the 5-bit channels) instead of at most half a level.
                            r5 = ((r * 31) + 127) / 255;
                            g6 = ((g * 63) + 127) / 255;
                            b5 = ((b * 31) + 127) / 255;
                        }

                        ushort rgb565 = (ushort)((r5 << 11) | (g6 << 5) | b5);

                        destPtr[0] = (byte)(rgb565 & 0xFF);       // LSB
                        destPtr[1] = (byte)((rgb565 >> 8) & 0xFF); // MSB

                        srcPtr += 4;   // advance 4 bytes (BGRA8888)
                        destPtr += 2;  // advance 2 bytes (RGB565)
                    }
                }
            }

            // Ensures the bitmap (owner of the native pixel buffer that srcPtr points
            // to) is not finalized during the loop → no use-after-free.
            GC.KeepAlive(bitmap);
        }
    }

    /// <summary>
    /// Draws a key in the "center" display area based on the given index.
    /// </summary>
    private async Task DrawKey(int index, SKBitmap bitmap, bool autoRefresh = true)
    {
        if (index < 0 || index >= Columns * Rows)
            throw new Exception($"Key {index} is not a valid key");

        if (VisibleX == null || Columns == 0)
            throw new Exception("VisibleX or Columns is not set");

        SKRectI rect = GetKeyRect(index);

        // DrawCanvas rejects a mismatch too; repeating it here is what names the key, which
        // is the part that identifies the caller that built the tile at the wrong size.
        if (bitmap == null || bitmap.Width != rect.Width || bitmap.Height != rect.Height)
            throw new ArgumentException(
                $"Key {index} needs a {rect.Width}x{rect.Height} bitmap, got " +
                $"{(bitmap == null ? "null" : $"{bitmap.Width}x{bitmap.Height}")}.", nameof(bitmap));

        await DrawCanvas("center", rect.Width, rect.Height, bitmap, rect.Left, rect.Top, autoRefresh);
    }

    /// <summary>
    /// True when repainting the grid one key at a time leaves pixels behind, because the
    /// keys do not cover the whole grid area. Such a device has to repaint the page as one
    /// region; see <see cref="DrawTouchGridRegion"/>.
    /// </summary>
    public bool KeyGridHasGaps => !KeyCalibration.TilesGaplessly;

    /// <summary>
    /// Repaints the whole key grid as a single region write, so the pixels between the keys
    /// are written too. Only for a full-page repaint: single-key updates and animation stay
    /// per key, which is a deliberate performance decision — one key changing must not cost
    /// a whole-grid write.
    ///
    /// Goes through <see cref="DrawCenterGridRegion"/> rather than the whole "center"
    /// buffer, so a device whose side strips share that buffer keeps them.
    /// </summary>
    public virtual async Task DrawTouchGridRegion(IReadOnlyList<TouchButton> buttons, LoupedeckConfig config)
    {
        if (buttons == null) return;

        int slots = Columns * Rows;
        SKBitmap[] tiles = new SKBitmap[slots];

        foreach (TouchButton button in buttons)
        {
            // Indices beyond the grid are the side strips, which this region does not cover
            // and whose own renderer owns them.
            if (button == null || button.Index < 0 || button.Index >= slots) continue;

            // The bitmap belongs to the button (RenderedImage owns its lifetime); compose
            // from it, never dispose it here.
            tiles[button.Index] =
                BitmapHelper.RenderTouchButtonContent(button, config, KeySize, KeySize,
                    GetWallpaperKeyRect(button.Index));
        }

        using SKBitmap region = BitmapHelper.ComposeTouchGrid(tiles, this);
        await DrawCenterGridRegion(region);
    }

    /// <summary>
    /// Draws a touch button on the corresponding key, optionally with an image and text overlay.
    /// </summary>
    public virtual async Task DrawTouchButton(
        TouchButton touchButton,
        LoupedeckConfig config,
        bool refresh)
    {
        ArgumentNullException.ThrowIfNull(touchButton);

        if (refresh || touchButton.RenderedImage == null)
        {
            var renderedBitmap =
                BitmapHelper.RenderTouchButtonContent(touchButton, config, KeySize, KeySize,
                    GetWallpaperKeyRect(touchButton.Index));
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
                SKRectI rect = GetKeyRect(slot);
                canvas.DrawBitmap(bmp, rect.Left, rect.Top, SKSamplingOptions.Default, paint: null);
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

        var renderedBitmap = BitmapHelper.RenderTextToBitmap(text, KeySize, KeySize);
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
    /// Pushes a pre-composed grid-region bitmap (Columns*KeySize × Rows*KeySize) to the "center"
    /// display at the touch grid's x-origin, leaving any side-strip regions of a unified
    /// buffer untouched. Used by the touch-page slide transition so a Razer's two side
    /// strips aren't clobbered. One framebuffer write + one refresh. The grid x-origin is
    /// <c>VisibleX[0]</c> here (unified 480-wide buffer); the CT overrides it to 0 because
    /// its "center" is a dedicated grid-only buffer.
    /// </summary>
    public virtual async Task DrawCenterGridRegion(SKBitmap gridBitmap, bool refresh = true)
    {
        ArgumentNullException.ThrowIfNull(gridBitmap);
        var xBase = VisibleX is { Length: > 0 } ? VisibleX[0] : 0;
        await DrawCanvasRegion("center", gridBitmap.Width, gridBitmap.Height, gridBitmap, xBase, 0, refresh);
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
    /// Sets the color of a button by its ID. A no-op on devices without addressable LED
    /// buttons — the reference driver throws there, but the shared controller drives this
    /// from the config's button list and a silent skip keeps every caller device-agnostic.
    /// </summary>
    public async Task SetButtonColor(Constants.ButtonType id, Color color)
    {
        if (!Geometry.HasLedButtons) return;

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
    /// Triggers a haptic vibration. A no-op on devices without a haptic motor: both call
    /// sites sit in the shared controller's touch handler, so gating here rather than there
    /// keeps a device with no motor from being sent SET_VIBRATION on every touch.
    /// </summary>
    public void Vibrate(byte pattern = Constants.VibrationPattern.Short)
    {
        if (!Geometry.HasVibration) return;

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