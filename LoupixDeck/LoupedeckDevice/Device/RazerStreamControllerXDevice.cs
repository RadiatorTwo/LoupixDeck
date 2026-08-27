using Avalonia.Media;

namespace LoupixDeck.LoupedeckDevice.Device;

/// <summary>
/// Razer Stream Controller X — a 5x3 grid of fifteen physical keys over one 480x288
/// panel. No knobs, no LED buttons, no side strips, and crucially no touchscreen:
/// the keys report as BUTTON_PRESS, not as TOUCH frames.
///
/// The wire protocol is otherwise identical to the Loupedeck Live S — same handshake,
/// same FRAMEBUFF/DRAW opcodes, same "\0M" display id. Only two numbers differ: the
/// keys are 96px rather than 90, and the panel is 288 tall rather than 270.
///
/// Because the whole application consumes touch events (and reads only
/// <c>TouchTarget.Key</c>), each key press is translated into a synthetic touch at the
/// centre of that key rather than reworking the pipeline. This mirrors what the
/// reference implementation at https://github.com/foxxyz/loupedeck does.
/// </summary>
public class RazerStreamControllerXDevice : LoupedeckDevice
{
    /// <summary>
    /// BUTTON_PRESS byte of the first key. The fifteen keys occupy 0x1b..0x29
    /// contiguously, in reading order (top-left to bottom-right).
    /// </summary>
    private const byte FirstKeyByte = 0x1b;

    /// <inheritdoc />
    public override int KeySize => 96;

    /// <inheritdoc />
    public override bool GridIsPhysicalKeys => true;

    /// <inheritdoc />
    /// <remarks>No haptic motor on this device.</remarks>
    public override bool SupportsVibration => false;

    public RazerStreamControllerXDevice(string host = null, string path = null, int baudrate = 0,
        bool autoConnect = true, int reconnectInterval = Constants.DefaultReconnectInterval)
        : base(host, path, baudrate, autoConnect, reconnectInterval)
    {
        // No addressable LED buttons — the fifteen keys are the touch grid, not simple buttons.
        Buttons = [];
        Columns = 5;
        Rows = 3;
        RotaryCount = 0;
        TouchButtonCount = Columns * Rows;
        // The grid spans the full panel; there is no bezel offset to skip.
        VisibleX = [0, 480];
        VisibleY = [0, 288];
        Type = "Razer Stream Controller X";
        ProductId = "0d09";
        Displays = new Dictionary<string, DisplayInfo>
        {
            ["center"] = new() { Id = "\0M"u8.ToArray(), Width = 480, Height = 288 }
        };
    }

    /// <inheritdoc />
    protected override bool TryMapButtonToTouchSlot(byte code, out int slot)
    {
        var index = code - FirstKeyByte;
        if (index < 0 || index >= Columns * Rows)
        {
            slot = -1;
            return false;
        }

        slot = index;
        return true;
    }

    /// <inheritdoc />
    protected override TouchTarget GetTarget(int x, int y)
    {
        // Clamping matters: Columns * KeySize == 480 == VisibleX[1], so an unclamped
        // x / KeySize at the right edge yields column 5 and therefore key 5, 10 or 15.
        var column = Math.Clamp(x / KeySize, 0, Columns - 1);
        var row = Math.Clamp(y / KeySize, 0, Rows - 1);
        return new TouchTarget { Screen = "center", Key = (row * Columns) + column };
    }

    /// <summary>
    /// The keys have no RGB backlight, so SET_COLOR is not supported. Swallowed rather
    /// than thrown: colours are applied in bulk over the configured simple buttons, and
    /// this device simply has none, so this is only reachable via a direct call.
    /// </summary>
    public override Task SetButtonColor(Constants.ButtonType id, Color color) => Task.CompletedTask;

    /// <summary>No haptic motor — SET_VIBRATION would be a frame the device cannot act on.</summary>
    public override void Vibrate(byte pattern = Constants.VibrationPattern.Short)
    {
    }
}
