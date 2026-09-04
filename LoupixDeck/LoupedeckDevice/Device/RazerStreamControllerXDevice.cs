using System.Collections.Frozen;
using LoupixDeck.Registry;

namespace LoupixDeck.LoupedeckDevice.Device;

/// <summary>
/// Razer Stream Controller X — fifteen physical keys in a 5x3 grid over a single 480x270
/// panel. No knobs, no LED buttons, no side strips, no haptic motor, and crucially no
/// touchscreen: the keys report as BUTTON_PRESS, not as TOUCH frames.
///
/// The wire protocol is identical to the Loupedeck Live S — same handshake, same
/// FRAMEBUFF/DRAW opcodes, same "\0M" display id, same 256000 baud. What differs is the
/// panel: rows 270..287 can be addressed but never reach the glass, and a FRAMEBUFF larger
/// than one full 480x270 frame is acknowledged and then dropped (see
/// <see cref="MaxFramebufferPayloadBytes"/>). Both were measured on hardware — a 480x288
/// full-panel write showed nothing at all, while the same image split into 480x270 + 480x18
/// appeared with the second band nowhere to be seen.
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

    /// <summary>A 480x270 panel; no strips, no motor, no LEDs.</summary>
    public static readonly DeviceGeometry KnownGeometry = new()
    {
        KeySize = 96,
        PanelWidth = 480,
        PanelHeight = 270,
        StripWidth = 0,
        PhysicalKeys = true,
        HasVibration = false,
        HasLedButtons = false
    };

    /// <inheritdoc />
    public override DeviceGeometry Geometry => KnownGeometry;

    /// <summary>
    /// One full 480x270 frame, in RGB565. Anything larger is acknowledged like a normal
    /// write and then never appears, so the limit has to be known here rather than
    /// discovered on the glass.
    /// </summary>
    public override int MaxFramebufferPayloadBytes => 480 * 270 * 2;

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

        // The whole panel is visible; unlike the Live S there is no bezel inset to skip.
        VisibleX = [0, 480];
        VisibleY = [0, 270];

        Type = "Razer Stream Controller X";
        ProductId = "0d09";

        Displays = new Dictionary<string, DisplayInfo>
        {
            ["center"] = new() { Id = "\0M"u8.ToArray(), Width = 480, Height = 270 }
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
