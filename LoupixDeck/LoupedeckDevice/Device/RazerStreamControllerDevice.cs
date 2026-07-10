using LoupixDeck.Models;
using SkiaSharp;

namespace LoupixDeck.LoupedeckDevice.Device;

/// <summary>
/// Razer Stream Controller — re-skinned Loupedeck Live with a different layout:
///   3 knobs on the left (KNOB_TL/CL/BL), 3 on the right (KNOB_TR/CR/BR),
///   4×3 touch grid in the centre (90×90 each, indices 0–11),
///   2 narrow touch panels behind the knobs (60×270, indices 12 left / 13 right),
///   8 physical LED buttons (BUTTON0–BUTTON7) below the screen.
///
/// Same wire protocol as the Loupedeck Live; the single physical 480×270 display
/// is rendered as left (X=0,60w) + center (X=60,360w) + right (X=420,60w) regions.
/// </summary>
public class RazerStreamControllerDevice : LoupedeckDevice
{
    /// <summary>Touch index for the left narrow panel.</summary>
    public const int LeftSideIndex = 12;

    /// <summary>Touch index for the right narrow panel.</summary>
    public const int RightSideIndex = 13;

    /// <inheritdoc />
    public override bool HasSideStrips => true;

    /// <summary>
    /// The panel discards bit 11 of the RGB565 word — the low bit of the red field — so its
    /// effective layout is R4-X1-G6-B5 and red carries 16 levels, not 32. Green (64 levels)
    /// and blue (32) resolve fully, matching the Loupedeck Live S.
    ///
    /// Confirmed on hardware with the colour-depth test pattern: the 32 transmitted red bars
    /// merge pairwise into 16 evenly across the whole range, the adjacent pair (2,3) shows no
    /// seam while (15,16) does, and — decisively — two bright blocks striping levels that
    /// differ ONLY in bit 11 (16/17 and 24/25) render perfectly flat, while a control block
    /// striping across the next bit up (23/24) stays visibly striped. Gamma cannot flatten
    /// the first two blocks and leave the control, at the same brightness, striped. The Live S
    /// shows stripes in all three.
    ///
    /// Dithering targets this grid; aiming at RGB565's nominal 5-bit red would put the whole
    /// pattern into the bit the panel throws away, which produces grain and no smoothing.
    /// </summary>
    public override (int Red, int Green, int Blue) PanelChannelBits => (4, 6, 5);

    /// <inheritdoc />
    /// <remarks>The left strip occupies panel x 0–60, so the centre grid starts at 60.</remarks>
    public override int WallpaperGridXOffset => 60;

    public RazerStreamControllerDevice(string host = null, string path = null, int baudrate = 0,
        bool autoConnect = true, int reconnectInterval = Constants.DefaultReconnectInterval)
        : base(host, path, baudrate, autoConnect, reconnectInterval)
    {
        Buttons = [0, 1, 2, 3, 4, 5, 6, 7];
        Columns = 4;
        Rows = 3;
        RotaryCount = 6;
        // 12 grid slots + 2 narrow side panels.
        TouchButtonCount = (Columns * Rows) + 2;
        // Centre grid sits between X=60 and X=420 on the unified 480px display.
        VisibleX = [60, 420];
        VisibleY = [0, 270];
        Type = "Razer Stream Controller";
        ProductId = "0d06";

        // Single unified display on the wire — the side regions are drawn at
        // offset X positions on the same "center" buffer (\0M).
        Displays = new Dictionary<string, DisplayInfo>
        {
            ["center"] = new() { Id = "\0M"u8.ToArray(), Width = 480, Height = 270 }
        };
    }

    protected override TouchTarget GetTarget(int x, int y)
    {
        if (VisibleX == null || VisibleY == null)
            throw new InvalidOperationException("VisibleX or VisibleY cannot be null.");

        // Left side panel.
        if (x < VisibleX[0])
            return new TouchTarget { Screen = "center", Key = LeftSideIndex };

        // Right side panel.
        if (x >= VisibleX[1])
            return new TouchTarget { Screen = "center", Key = RightSideIndex };

        // Centre 4×3 grid — clamp and translate into grid coords.
        x = Math.Clamp(x, VisibleX[0], VisibleX[1]) - VisibleX[0];
        y = Math.Clamp(y, VisibleY[0], VisibleY[1]);
        var column = x / 90;
        var row = y / 90;
        var key = (row * Columns) + column;
        return new TouchTarget { Screen = "center", Key = key };
    }

    /// <summary>
    /// Draws an arbitrary bitmap to one touch slot — handles the 60×270 side
    /// panels (12/13) by routing to their unified-display X offsets; everything
    /// else falls through to the base 90×90 grid path.
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
            catch (Exception ex) { Console.WriteLine($"Razer side-panel slot draw failed for index {index}: {ex.Message}"); }
            return;
        }
        await base.DrawTouchSlot(index, bitmap, refresh);
    }

    /// <summary>
    /// Overrides the base grid renderer for the 4×3 centre grid. The side panels
    /// (indices 12/13) are NOT painted from the touch-button pipeline: in segmented
    /// mode they show the adjacent dial labels, driven by the controller via
    /// <see cref="DrawTouchSlot"/> on rotary-page changes. Skipping them here keeps
    /// touch-page redraws from overwriting the rotary labels.
    /// </summary>
    public override async Task DrawTouchButton(TouchButton touchButton, LoupedeckConfig config, bool refresh, int columns)
    {
        ArgumentNullException.ThrowIfNull(touchButton);

        if (touchButton.Index < Columns * Rows)
        {
            await base.DrawTouchButton(touchButton, config, refresh, columns);
            return;
        }

        // Side panels are owned by the rotary-label renderer; ignore here.
    }
}
