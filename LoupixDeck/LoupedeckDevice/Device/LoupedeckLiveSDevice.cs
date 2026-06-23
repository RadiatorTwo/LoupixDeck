using System.Collections.Immutable;

namespace LoupixDeck.LoupedeckDevice.Device;

public class LoupedeckLiveSDevice : LoupedeckDevice
{
    private static readonly ImmutableDictionary<string, DisplayInfo> displays = ImmutableDictionary.CreateRange<string, DisplayInfo>(
    [
        new("center", new DisplayInfo("\0M"u8, 480, 270))
    ]);

    protected override ImmutableDictionary<string, DisplayInfo> Displays => displays;
    public override int[] Buttons { get; } = InitSimpleArray(4);
    public override int Columns => 5;
    public override int Rows => 3;
#nullable enable
    protected override int[]? VisibleX { get; } = [15, 464];
    protected override int[]? VisibleY { get; } = [10, 269];
#nullable restore
    public override int RotaryCount => 2;
    public override string Type => "Loupedeck Live S";
    public override string ProductId => "0006";

    public override int TouchButtonCount => Columns * Rows;

    public LoupedeckLiveSDevice(string host = null, string path = null, int baudrate = 0, bool autoConnect = true, int reconnectInterval = Constants.DefaultReconnectInterval)
        : base(host, path, baudrate, autoConnect, reconnectInterval)
    {
    }

    // The Live S has two rotaries stacked vertically on the left of the touch
    // grid: the top one sits next to slot 0 (row 0, col 0), the bottom one
    // next to slot 5 (row 1, col 0). See Views/Devices/LoupedeckLiveSLayout.axaml.
    public override int GetTouchSlotForRotary(int rotaryIndex) => rotaryIndex switch
    {
        0 => 0,
        1 => 5,
        _ => -1
    };

    protected override TouchTarget GetTarget(int x, int y)
    {
        if (VisibleX == null || VisibleY == null)
        {
            throw new InvalidOperationException("VisibleX or VisibleY cannot be null.");
        }

        x = Math.Max(x, VisibleX[0]);
        x = Math.Min(x, VisibleX[1]);
        y = Math.Max(y, VisibleY[0]);
        y = Math.Min(y, VisibleY[1]);
        x -= VisibleX[0];
        y -= VisibleY[0];
        var column = x / 90;
        var row = y / 90;
        var key = (row * Columns) + column;
        return new TouchTarget { Screen = "center", Key = key };
    }
}