namespace LoupixDeck.LoupedeckDevice.Device;

public class LoupedeckLiveSDevice : LoupedeckDevice
{
    public LoupedeckLiveSDevice(string host = null, string path = null, bool autoConnect = true, int reconnectInterval = Constants.DefaultReconnectInterval)
        : base(host, path, autoConnect, reconnectInterval)
    {
        Buttons = [0, 1, 2, 3];
        Columns = 5;
        Rows = 3;
        VisibleX = [15, 464];
        VisibleY = [10, 269];
        Type = "Loupedeck Live S";
        ProductId = "0006";
        Displays = new Dictionary<string, DisplayInfo>
        {
            ["center"] = new() { Id = "\0M"u8.ToArray(), Width = 480, Height = 270 }
        };
    }

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
        var key = row * Columns + column;
        return new TouchTarget { Screen = "center", Key = key };
    }
}