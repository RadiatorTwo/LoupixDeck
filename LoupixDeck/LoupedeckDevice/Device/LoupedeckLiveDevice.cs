namespace LoupixDeck.LoupedeckDevice.Device;

/// <summary>
/// Loupedeck Live — the original device (VID 2ec2:0004).
/// </summary>
/// <remarks>
/// <para>
/// Hardware-identical to the <see cref="RazerStreamControllerDevice"/>:<br/>
/// <list type="bullet">
///   <item>
///     A single 480×270 display rendered as left (X=0,60w), centre 4×3 grid (X=60,360w), right (X=420,60w) regions
///   </item>
///   <item>
///     6 rotary encoders (3 left, 3 right)
///   </item>
///   <item>
///     8 physical LED buttons
///   </item>
/// </list>
/// Because the wire protocol, display layout and side-strip behaviour are the same,
/// this simply reuses the Razer implementation and only re-labels the
/// device identity (<see cref="LoupedeckDevice.Type"/> / <see cref="LoupedeckDevice.ProductId"/>).
/// </para>
/// <para>
/// The side displays ARE touch-capable on the Live
/// (only Loupedeck's own software chose not to use them)
/// so the inherited side-strip touch + swipe-paging behaviour applies unchanged.
/// </para>
/// </remarks>
public class LoupedeckLiveDevice(string host = null, string path = null, int baudrate = 0,
    bool autoConnect = true, int reconnectInterval = Constants.DefaultReconnectInterval) : RazerStreamControllerDevice(host, path, baudrate, autoConnect, reconnectInterval)
{
    public override string Type => "Loupedeck Live";
    public override string ProductId => "0004";
}
