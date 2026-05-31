using LoupixDeck.Models;
using LoupixDeck.Utils;
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

    public RazerStreamControllerDevice(string host = null, string path = null, int baudrate = 0,
        bool autoConnect = true, int reconnectInterval = Constants.DefaultReconnectInterval)
        : base(host, path, baudrate, autoConnect, reconnectInterval)
    {
        Buttons = [0, 1, 2, 3, 4, 5, 6, 7];
        Columns = 4;
        Rows = 3;
        RotaryCount = 6;
        // 12 grid slots + 2 narrow side panels.
        TouchButtonCount = Columns * Rows + 2;
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
        var key = row * Columns + column;
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
    /// Overrides the base grid renderer so indices 12/13 paint the 60×270 side
    /// panels at their unified-display X offsets. Other indices fall through to
    /// the base 90×90 grid path (which honours Columns/VisibleX from this class).
    /// </summary>
    public override async Task DrawTouchButton(TouchButton touchButton, LoupedeckConfig config, bool refresh, int columns)
    {
        ArgumentNullException.ThrowIfNull(touchButton);

        if (touchButton.Index < Columns * Rows)
        {
            await base.DrawTouchButton(touchButton, config, refresh, columns);
            return;
        }

        if (touchButton.Index != LeftSideIndex && touchButton.Index != RightSideIndex)
            return;

        const int sideW = 60;
        const int sideH = 270;

        if (refresh || touchButton.RenderedImage == null)
        {
            var rendered = BitmapHelper.RenderTouchButtonContent(touchButton, config, sideW, sideH, columns);
            if (rendered == null) return;
        }

        if (touchButton.RenderedImage == null) return;

        try
        {
            var destX = touchButton.Index == LeftSideIndex ? 0 : 420;
            await DrawCanvas("center", sideW, sideH, touchButton.RenderedImage, destX, 0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Razer side-panel draw failed for index {touchButton.Index}: {ex.Message}");
        }
    }
}
