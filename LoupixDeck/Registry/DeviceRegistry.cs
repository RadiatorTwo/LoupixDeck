using System.Text;
using LoupixDeck.LoupedeckDevice.Device;

namespace LoupixDeck.Registry;

public static class DeviceRegistry
{
    /// <param name="Geometry">
    /// Pixel geometry of this model. Carried on the registry entry — not read from a live
    /// <see cref="LoupixDeck.LoupedeckDevice.Device.LoupedeckDevice"/> — because services are
    /// built before the device object exists (and it never appears at all without hardware).
    /// </param>
    /// <param name="Baudrate">
    /// Serial rate this model is opened at. Carried here rather than inside the device class
    /// so the connection rate is resolved before the device object exists — the same reason
    /// <paramref name="Geometry"/> lives on the entry. Defaults to
    /// <see cref="LoupixDeck.LoupedeckDevice.Constants.DefaultBaudrate"/>; a model that needs
    /// a different rate states it on its own entry.
    /// </param>
    public record DeviceInfo(
        string Name,
        string VendorId,
        string ProductId,
        Type DeviceType,
        DeviceGeometry Geometry,
        int Baudrate = LoupixDeck.LoupedeckDevice.Constants.DefaultBaudrate)
    {
        /// <summary>
        /// Filesystem-safe slug derived from the device name. Used to scope the
        /// per-device config file (e.g. "loupedeck-live-s" → config_loupedeck-live-s.json).
        /// </summary>
        public string Slug => Slugify(Name);
    }

    public static readonly List<DeviceInfo> SupportedDevices =
    [
        new("Loupedeck Live", "2ec2", "0004", typeof(LoupedeckLiveDevice), RazerStreamControllerDevice.KnownGeometry),
        new("Loupedeck Live S", "2ec2", "0006", typeof(LoupedeckLiveSDevice), LoupedeckLiveSDevice.KnownGeometry),
        new("Razer Stream Controller", "1532", "0d06", typeof(RazerStreamControllerDevice), RazerStreamControllerDevice.KnownGeometry),
        new("Razer Stream Controller X", "1532", "0d09", typeof(RazerStreamControllerXDevice), RazerStreamControllerXDevice.KnownGeometry),
        new("Loupedeck CT", "2ec2", "0003", typeof(LoupedeckCtDevice), LoupedeckCtDevice.KnownGeometry),
        new("Loupedeck CT", "2ec2", "0007", typeof(LoupedeckCtDevice), LoupedeckCtDevice.KnownGeometry)
    ];

    public static DeviceInfo GetDeviceByVidPid(string vid, string pid)
    {
        if (string.IsNullOrEmpty(vid) || string.IsNullOrEmpty(pid)) return null;
        return SupportedDevices.FirstOrDefault(d =>
            string.Equals(d.VendorId, vid, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(d.ProductId, pid, StringComparison.OrdinalIgnoreCase));
    }

    private static string Slugify(string name)
    {
        var sb = new StringBuilder(name.Length);
        var lastDash = false;
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                lastDash = false;
            }
            else if (!lastDash && sb.Length > 0)
            {
                sb.Append('-');
                lastDash = true;
            }
        }
        if (lastDash) sb.Length--;
        return sb.ToString();
    }
}
