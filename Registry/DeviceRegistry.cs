using System.Text;
using LoupixDeck.LoupedeckDevice.Device;

namespace LoupixDeck.Registry;

public static class DeviceRegistry
{
    public record DeviceInfo(string Name, string VendorId, string ProductId, Type DeviceType)
    {
        /// <summary>
        /// Filesystem-safe slug derived from the device name. Used to scope the
        /// per-device config file (e.g. "loupedeck-live-s" → config_loupedeck-live-s.json).
        /// </summary>
        public string Slug => Slugify(Name);
    }

    public static readonly List<DeviceInfo> SupportedDevices =
    [
        new("Loupedeck Live", "2ec2", "0004", typeof(LoupedeckLiveDevice)),
        new("Loupedeck Live S", "2ec2", "0006", typeof(LoupedeckLiveSDevice)),
        new("Razer Stream Controller", "1532", "0d06", typeof(RazerStreamControllerDevice))
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
