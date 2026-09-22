using System.Collections.Frozen;
using LoupixDeck.Models;
using LoupixDeck.Registry;
using LoupixDeck.Utils;
using SkiaSharp;

namespace LoupixDeck.LoupedeckDevice.Device;

/// <summary>
/// Loupedeck CT — the most complex device in the family. Unlike Live/Razer, which
/// share one unified framebuffer addressed by X-offset, the CT exposes FOUR
/// two framebuffers: the unified "center" (480x270, id "\0M") carrying both side
/// strips and the 4x3 grid exactly as on Live/Razer, and "knob" — the round 240x240
/// touchscreen embedded in the large centre dial ("the wheel"), which the firmware
/// expects in big-endian pixel order (see <see cref="DisplayInfo.BigEndianPixels"/>).
///
/// Geometry: 4x3 touch grid (indices 0-11), 2 side strips (12/13, same pattern as
/// <see cref="RazerStreamControllerDevice"/>), 6 side dials + 1 centre wheel dial
/// (7 rotaries total), 8 round LED buttons + 12 named square buttons (20 simple
/// buttons total).
///
/// Protocol confirmed via a hardware serial trace (LOUPIXDECK_DEBUG_PROTOCOL=1,
/// 2026-06-18): the 12 named buttons' byte codes (0x0f-0x1a) matched the
/// community-driver-derived guesses exactly; the wheel's KNOB_ROTATE byte is 0x00
/// (corrected from an initial 0x1b guess); the wheel has no separate "click" byte
/// — pressing it shows up as a tight cluster of touch start/end events near the
/// centre of its own screen (Constants.Command.WHEEL_TOUCH/WHEEL_TOUCH_END,
/// wired to <see cref="LoupedeckDevice.OnWheelTouch"/>). Still unconfirmed/unwired:
/// the big-endian "knob" framebuffer, and turning the wheel's touch cluster into an
/// actual click command (no consumer wiring yet — see the CT support plan).
/// </summary>
public class LoupedeckCtDevice : LoupedeckDevice
{
    /// <summary>Touch index for the left narrow panel.</summary>
    public const int LeftSideIndex = 12;

    /// <summary>Touch index for the right narrow panel.</summary>
    public const int RightSideIndex = 13;

    /// <inheritdoc />
    public override bool HasSideStrips => true;

    /// <summary>
    /// 90px keys on a 480x270 panel with 60px side strips. The panel stays 480 wide even
    /// though the CT draws it through four separate framebuffers (60 left + 360 centre +
    /// 60 right): touch coordinates and the page wallpaper are unified across all three.
    /// </summary>
    public static readonly DeviceGeometry KnownGeometry = new()
    {
        KeySize = 90,
        PanelWidth = 480,
        PanelHeight = 270,
        StripWidth = 60,
        Columns = 4,
        Rows = 3
    };

    /// <inheritdoc />
    public override DeviceGeometry Geometry => KnownGeometry;

    /// <inheritdoc />
    /// <remarks>
    /// This offset selects which slice of a continuous wallpaper bitmap is cropped for the
    /// grid (see <see cref="Utils.BitmapHelper.RenderTouchButtonContent"/>). 60 — same as
    /// Razer — because CT wallpapers are one continuous 480px-wide image spanning both side
    /// strips and the grid, which matches the unified framebuffer the hardware actually has.
    /// </remarks>
    public override int WallpaperGridXOffset => 60;

    public LoupedeckCtDevice(string host = null, string path = null, int baudrate = 0,
        bool autoConnect = true, int reconnectInterval = Constants.DefaultReconnectInterval)
        : base(host, path, baudrate, autoConnect, reconnectInterval)
    {
        // 8 round + 12 named square buttons = 20 simple buttons. The wheel is a
        // rotary (handled via RotaryCount/TryGetRotaryIndex), not a simple button.
        Buttons = Enumerable.Range(0, 20).ToArray();
        Columns = 4;
        Rows = 3;
        RotaryCount = 7; // 6 side dials + 1 centre wheel
        TouchButtonCount = (Columns * Rows) + 2; // 12 grid slots + 2 side strips
        VisibleX = [60, 420];
        VisibleY = [0, 270];
        Type = "Loupedeck CT";
        ProductId = "0003";
        VendorId = "2ec2";

        // One unified 480x270 buffer for the strips and the grid, exactly as on
        // Live/Razer, plus the wheel's own screen. Verified on hardware: the strips are
        // regions of "\0M" at x=0 and x=420, not separate "\0L"/"\0R" framebuffers.
        Displays = new Dictionary<string, DisplayInfo>
        {
            ["center"] = new() { Id = "\0M"u8.ToArray(), Width = 480, Height = 270 },
            // The wheel is the one framebuffer that wants MSB-first pixels.
            ["knob"] = new() { Id = "\0W"u8.ToArray(), Width = 240, Height = 240, BigEndianPixels = true }
        }.ToFrozenDictionary();
    }

    /// <summary>
    /// Touch coordinates are unified across left strip / centre grid / right strip
    /// (confirmed by both reference drivers), even though each is its own
    /// framebuffer for drawing. Same formula as <see cref="RazerStreamControllerDevice"/>.
    /// </summary>
    protected override TouchTarget GetTarget(int x, int y)
    {
        if (VisibleX == null || VisibleY == null)
            throw new InvalidOperationException("VisibleX or VisibleY cannot be null.");

        if (x < VisibleX[0])
            return new TouchTarget { Screen = "center", Key = LeftSideIndex };

        if (x >= VisibleX[1])
            return new TouchTarget { Screen = "center", Key = RightSideIndex };

        x = Math.Clamp(x, VisibleX[0], VisibleX[1]) - VisibleX[0];
        y = Math.Clamp(y, VisibleY[0], VisibleY[1]) - VisibleY[0];
        // Nearest key centre rather than a division by the key size: a calibrated grid
        // can have a pitch that differs from the key size, and a plain division then
        // reports a column past the last one for touches on the right-hand keys.
        int column = KeyCalibration.NearestColumn(x, Columns);
        int row = KeyCalibration.NearestRow(y, Rows);
        var key = (row * Columns) + column;
        return new TouchTarget { Screen = "center", Key = key };
    }

    /// <summary>
    /// Routes the side strips to their X offsets on the unified "center" buffer, the same
    /// way <see cref="RazerStreamControllerDevice"/> does; grid slots fall through to the
    /// base class, whose GridOriginX offset is now correct for this device.
    /// </summary>
    public override async Task DrawTouchSlot(int index, SKBitmap bitmap, bool refresh = true)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        if (index == LeftSideIndex || index == RightSideIndex)
        {
            const int sideW = 60;
            const int sideH = 270;
            var destX = index == LeftSideIndex ? 0 : 420;
            try { await DrawCanvasRegion("center", sideW, sideH, bitmap, destX, 0, refresh); }
            catch (Exception ex) { Console.WriteLine($"CT side-panel slot draw failed for index {index}: {ex.Message}"); }
            return;
        }

        await base.DrawTouchSlot(index, bitmap, refresh);
    }

    /// <inheritdoc />
    /// <remarks>Side panels (12/13) are owned by the rotary-label renderer, same as Razer.</remarks>
    public override async Task DrawTouchButton(TouchButton touchButton, LoupedeckConfig config, bool refresh)
    {
        ArgumentNullException.ThrowIfNull(touchButton);

        if (touchButton.Index >= Columns * Rows)
            return; // side panels — not owned by the grid touch-button pipeline

        await base.DrawTouchButton(touchButton, config, refresh);
    }
}
