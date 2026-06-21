using LoupixDeck.Models;
using LoupixDeck.Utils;
using SkiaSharp;

namespace LoupixDeck.LoupedeckDevice.Device;

/// <summary>
/// Loupedeck CT — the most complex device in the family. Unlike Live/Razer, which
/// share one unified framebuffer addressed by X-offset, the CT exposes FOUR
/// independent framebuffers: "center" (360x270), "left"/"right" side strips
/// (60x270 each), and "knob" — the round 240x240 touchscreen embedded in the
/// large centre dial ("the wheel"), which the firmware expects in big-endian
/// pixel order (not yet implemented here — drawing to it will produce garbled
/// colors until <see cref="LoupedeckDevice.ConvertSKBitmapToRaw16BppUnsafe"/> /
/// <see cref="DisplayInfo"/> gain an endianness flag).
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

    /// <inheritdoc />
    /// <remarks>
    /// This offset only affects which slice of a continuous wallpaper bitmap is
    /// cropped for the grid (see <see cref="Utils.BitmapHelper.RenderTouchButtonContent"/>);
    /// it is unrelated to the framebuffer write position, which is always 0 for the
    /// CT's dedicated "center" buffer (see <see cref="DrawTouchButtonAt"/>). Kept at
    /// 60 — same as Razer — on the assumption that CT wallpapers are authored as one
    /// continuous 480px-wide image spanning both side strips and the grid; revisit
    /// once real hardware/wallpaper behaviour can be observed.
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

        // Four independent framebuffers (unlike Live/Razer's single unified one).
        Displays = new Dictionary<string, DisplayInfo>
        {
            ["center"] = new() { Id = "\0A"u8.ToArray(), Width = 360, Height = 270 },
            ["left"] = new() { Id = "\0L"u8.ToArray(), Width = 60, Height = 270 },
            ["right"] = new() { Id = "\0R"u8.ToArray(), Width = 60, Height = 270 },
            // VERIFY ON HARDWARE: firmware expects this buffer big-endian; the base
            // class's pixel converter is little-endian-only today (Phase 2 work).
            ["knob"] = new() { Id = "\0W"u8.ToArray(), Width = 240, Height = 240 }
        };
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
        y = Math.Clamp(y, VisibleY[0], VisibleY[1]);
        var column = x / 90;
        var row = y / 90;
        var key = (row * Columns) + column;
        return new TouchTarget { Screen = "center", Key = key };
    }

    /// <summary>
    /// Routes side-strip slots to their own dedicated "left"/"right" framebuffers
    /// (each at origin 0,0 — simpler than Razer's X-offset-into-one-buffer trick,
    /// since the CT gives each strip its own buffer); grid slots go to "center" at
    /// their own 0-based position (NOT the base class's VisibleX[0]-offset DrawKey,
    /// which assumes a unified buffer the CT doesn't have).
    /// </summary>
    public override async Task DrawTouchSlot(int index, SKBitmap bitmap, bool refresh = true)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        if (index == LeftSideIndex || index == RightSideIndex)
        {
            var displayId = index == LeftSideIndex ? "left" : "right";
            try { await DrawCanvasRegion(displayId, 60, 270, bitmap, 0, 0, refresh); }
            catch (Exception ex) { Console.WriteLine($"CT side-panel slot draw failed for index {index}: {ex.Message}"); }
            return;
        }

        if (index < 0 || index >= Columns * Rows)
            throw new ArgumentOutOfRangeException(nameof(index), $"Key {index} is not a valid grid key");

        await DrawTouchButtonAt(index, bitmap, refresh);
    }

    /// <inheritdoc />
    /// <remarks>Side panels (12/13) are owned by the rotary-label renderer, same as Razer.</remarks>
    public override async Task DrawTouchButton(TouchButton touchButton, LoupedeckConfig config, bool refresh, int columns)
    {
        ArgumentNullException.ThrowIfNull(touchButton);

        if (touchButton.Index >= Columns * Rows)
            return; // side panels — not owned by the grid touch-button pipeline

        if (refresh || touchButton.RenderedImage == null)
        {
            var renderedBitmap =
                BitmapHelper.RenderTouchButtonContent(touchButton, config, 90, 90, columns, WallpaperGridXOffset);
            if (renderedBitmap == null) return;
        }

        await DrawTouchButtonAt(touchButton.Index, touchButton.RenderedImage, refresh);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The base implementation offsets slots by VisibleX[0] (=60) into a unified
    /// buffer; the CT's "center" buffer is its own dedicated 360-wide framebuffer
    /// starting at 0, so that offset would draw the grid partly off-canvas. This
    /// override is identical to the base except xBase is always 0.
    /// </remarks>
    public override async Task DrawTouchSlotsAtomic(IReadOnlyList<SKBitmap> slotBitmaps, bool refresh = true)
    {
        if (slotBitmaps == null || slotBitmaps.Count == 0) return;
        var (width, height) = GetDisplaySize("center");
        if (width == 0) return;

        const int keySize = 90;

        using var full = new SKBitmap(new SKImageInfo(width, height,
            SKColorType.Bgra8888, SKAlphaType.Premul));

        lock (SkiaRenderGate.Sync)
        {
            using var canvas = new SKCanvas(full);
            canvas.Clear(SKColors.Black);
            for (var slot = 0; slot < slotBitmaps.Count && slot < Columns * Rows; slot++)
            {
                var bmp = slotBitmaps[slot];
                if (bmp == null) continue;
                var x = (slot % Columns) * keySize;
                var y = (slot / Columns) * keySize;
                canvas.DrawBitmap(bmp, x, y);
            }
        }

        try
        {
            await DrawCanvasRegion("center", width, height, full, 0, 0, refresh);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DrawTouchSlotsAtomic (CT) failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Writes a 90x90 bitmap to the "center" buffer at the grid position for
    /// <paramref name="index"/>, using a 0-based origin (the CT's center buffer is
    /// its own dedicated 360-wide framebuffer — unlike Razer's unified 480-wide one,
    /// it does NOT start at VisibleX[0]).
    /// </summary>
    private async Task DrawTouchButtonAt(int index, SKBitmap bitmap, bool refresh)
    {
        var x = (index % Columns) * 90;
        var y = (index / Columns) * 90;

        try
        {
            await DrawCanvasRegion("center", 90, 90, bitmap, x, y, refresh);
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
}
