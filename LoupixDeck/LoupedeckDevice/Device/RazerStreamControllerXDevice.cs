using System.Collections.Frozen;
using LoupixDeck.Registry;

namespace LoupixDeck.LoupedeckDevice.Device;

/// <summary>
/// Razer Stream Controller X — fifteen physical keys in a 5x3 grid over a single 480x288
/// panel. No knobs, no LED buttons, no side strips, no haptic motor, and crucially no
/// touchscreen: the keys report as BUTTON_PRESS, not as TOUCH frames.
///
/// The wire protocol is otherwise identical to the Loupedeck Live S — same handshake, same
/// FRAMEBUFF/DRAW opcodes, same "\0M" display id, same 256000 baud. Two numbers differ: the
/// keys are 96px rather than 90, and the panel is 288 tall rather than 270. The grid spans
/// the whole panel, so there is no bezel offset to skip either.
///
/// Because the application consumes touches everywhere, each key press is translated into a
/// synthetic touch at the centre of that key (see
/// <see cref="LoupedeckDevice.TryGetPhysicalKeySlot"/>) rather than reworking that pipeline.
/// This mirrors the reference implementation at https://github.com/foxxyz/loupedeck.
/// </summary>
public sealed class RazerStreamControllerXDevice : LoupedeckDevice
{
    /// <summary>
    /// BUTTON_PRESS byte of the first key. The fifteen keys occupy 0x1b..0x29 contiguously,
    /// in reading order (top-left to bottom-right). These bytes are deliberately absent from
    /// <see cref="Constants.Buttons"/>; they are unrelated to the outgoing SET_VIBRATION
    /// command, which happens to share the value 0x1b in a different enum.
    /// </summary>
    private const byte FirstKeyByte = 0x1b;

    /// <summary>96px keys spanning the full 480x288 panel; no strips, no motor, no LEDs.</summary>
    public static readonly DeviceGeometry KnownGeometry = new()
    {
        KeySize = 96,
        PanelWidth = 480,
        PanelHeight = 288,
        StripWidth = 0,
        PhysicalKeys = true,
        HasVibration = false,
        HasLedButtons = false
    };

    /// <inheritdoc />
    public override DeviceGeometry Geometry => KnownGeometry;

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

        // The grid spans the full panel; unlike the Live S there is no bezel inset.
        VisibleX = [0, 480];
        VisibleY = [0, 288];

        Type = "Razer Stream Controller X";
        ProductId = "0d09";

        Displays = new Dictionary<string, DisplayInfo>
        {
            ["center"] = new() { Id = "\0M"u8.ToArray(), Width = 480, Height = 288 }
        }.ToFrozenDictionary();
    }

    /// <inheritdoc />
    protected override bool TryGetPhysicalKeySlot(byte raw, out int slot)
    {
        slot = raw - FirstKeyByte;
        if (slot >= 0 && slot < Columns * Rows) return true;

        slot = -1;
        return false;
    }

    /// <summary>
    /// Maps a panel coordinate to a grid slot. Only ever reached through the synthetic
    /// touches the key emulation produces — the device has no touchscreen of its own.
    /// </summary>
    protected override TouchTarget GetTarget(int x, int y)
    {
        if (VisibleX == null || VisibleY == null)
            throw new InvalidOperationException("VisibleX or VisibleY cannot be null.");

        x = Math.Clamp(x, VisibleX[0], VisibleX[1]) - VisibleX[0];
        y = Math.Clamp(y, VisibleY[0], VisibleY[1]) - VisibleY[0];

        int column = Math.Min(x / KeySize, Columns - 1);
        int row = Math.Min(y / KeySize, Rows - 1);

        return new TouchTarget
        {
            Screen = "center",
            Key = (row * Columns) + column
        };
    }
}
