namespace LoupixDeck.LoupedeckDevice.Device;

/// <summary>
/// Loupedeck Live — the original device (VID 2ec2:0004).
///
/// Hardware-identical to the <see cref="RazerStreamControllerDevice"/>: a single
/// 480×270 display rendered as left (X=0,60w) + centre 4×3 grid (X=60,360w) +
/// right (X=420,60w) regions, 6 rotary encoders (3 left, 3 right) and 8 physical
/// LED buttons. Because the wire protocol, display layout and side-strip behaviour
/// are the same, this simply reuses the Razer implementation and only re-labels the
/// device identity (<see cref="LoupedeckDevice.Type"/> / <see cref="LoupedeckDevice.ProductId"/>).
///
/// The side displays ARE touch-capable on the Live (only Loupedeck's own software
/// chose not to use them), so the inherited side-strip touch + swipe-paging behaviour
/// applies unchanged.
/// </summary>
public class LoupedeckLiveDevice : RazerStreamControllerDevice
{
    public LoupedeckLiveDevice(string host = null, string path = null, int baudrate = 0,
        bool autoConnect = true, int reconnectInterval = Constants.DefaultReconnectInterval)
        : base(host, path, baudrate, autoConnect, reconnectInterval)
    {
        Type = "Loupedeck Live";
        ProductId = "0004";
    }
}
