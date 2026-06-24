using System.Collections.Immutable;
using LoupixDeck.Models;
using SkiaSharp;

namespace LoupixDeck.LoupedeckDevice.Device;

/// <summary>
/// Razer Stream Controller — re-skinned Loupedeck Live with a different layout:
/// <list type="bullet">
///   <item>3 knobs on the left (KNOB_TL/CL/BL), 3 on the right (KNOB_TR/CR/BR)</item>
///   <item>4×3 touch grid in the centre (90×90 each, indices 0–11)</item>
///   <item>2 narrow touch panels behind the knobs (60×270, indices 12 left / 13 right)</item>
///   <item>8 physical LED buttons (BUTTON0–BUTTON7) below the screen</item>
/// </list>
/// </summary>
/// <remarks>
/// Same wire protocol as the Loupedeck Live; the single physical 480×270 display
/// is rendered as left (X=0,60w) + center (X=60,360w) + right (X=420,60w) regions.
/// </remarks>
public class RazerStreamControllerDevice : LoupedeckDevice
{
    /// <summary>Touch index for the left narrow panel.</summary>
    public const int LeftSideIndex = 12;

    /// <summary>Touch index for the right narrow panel.</summary>
    public const int RightSideIndex = 13;

    // Single unified display on the wire — the side regions are drawn at
    // offset X positions on the same "center" buffer (\0M).
    private static readonly ImmutableDictionary<string, DisplayInfo> displays = ImmutableDictionary.CreateRange<string, DisplayInfo>(
    [
        new("center", new DisplayInfo("\0M"u8, 480, 270))
    ]);

    protected override ImmutableDictionary<string, DisplayInfo> Displays => displays;
    public override int[] Buttons { get; } = InitSimpleArray(8);
    public override int Columns => 4;
    public override int Rows => 3;
#nullable enable
    // Centre grid sits between X=60 and X=420 on the unified 480px display.
    protected override int[]? VisibleX { get; } = [60, 420];
    protected override int[]? VisibleY { get; } = [0, 270];
#nullable restore
    public override int RotaryCount => 6;
    public override string Type => "Razer Stream Controller";
    public override string ProductId => "0d06";

    /// <inheritdoc />
    public override bool HasSideStrips => true;

    /// <inheritdoc />
    /// <remarks>The left strip occupies panel x 0–60, so the centre grid starts at 60.</remarks>
    public override int WallpaperGridXOffset => 60;

    // 12 grid slots + 2 narrow side panels.
    public override int TouchButtonCount => (Columns * Rows) + 2;

    public RazerStreamControllerDevice(string host = null, string path = null, int baudrate = 0,
        bool autoConnect = true, int reconnectInterval = Constants.DefaultReconnectInterval)
        : base(host, path, baudrate, autoConnect, reconnectInterval)
    {
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
