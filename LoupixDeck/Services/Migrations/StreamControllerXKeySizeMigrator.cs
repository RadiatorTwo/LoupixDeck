using LoupixDeck.Registry;
using Newtonsoft.Json.Linq;

namespace LoupixDeck.Services.Migrations;

/// <summary>
/// Config migration v9 → v10: rescales a Razer Stream Controller X config from the 96px key
/// the app used to assume to the 74px it actually has.
///
/// v1.21.0-beta.5 shipped the Stream Controller X with <c>KeySize = 96</c>, derived from
/// dividing the panel it was believed to have (480x288) by the 5x3 grid. Hardware says
/// otherwise: the panel shows 480x270, the keys sit in a 98px pitch, and only 74px of each
/// key is visible — at 75 the outermost row of a test pattern already disappears. So every
/// layer value in such a config is expressed in a tile that is 96 wide but should be 74, and
/// a design authored on that build renders about 30% too large.
///
/// The correction applies to the whole file rather than to individual values, because on
/// that build the tile simply was 96: a value got there either through the v8 → v9 migration
/// (multiplied by 96/90) or by being authored against a 96px editor tile, and both are in
/// the same space. There is no marker in the file that would distinguish them, and none is
/// needed.
///
/// Every other device is untouched — this is the one model whose key size was wrong.
/// </summary>
public sealed class StreamControllerXKeySizeMigrator : IConfigMigration
{
    /// <summary>Product id of the Stream Controller X, matched via the config's file name.</summary>
    private const string StreamControllerXPid = "0d09";

    /// <summary>The key size v1.21.0-beta.5 assumed for this device.</summary>
    private const double AssumedKeySize = 96.0;

    public int FromVersion => 9;

    public void Apply(JObject root, string configFilePath)
    {
        DeviceRegistry.DeviceInfo device = TouchButtonLayerScaler.ResolveDevice(configFilePath);

        if (device != null &&
            string.Equals(device.ProductId, StreamControllerXPid, StringComparison.OrdinalIgnoreCase))
        {
            TouchButtonLayerScaler.ScaleAll(root, device.Geometry.KeySize / AssumedKeySize);
        }

        root["Version"] = FromVersion + 1;
    }
}
