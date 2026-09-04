using LoupixDeck.Registry;
using Newtonsoft.Json.Linq;

namespace LoupixDeck.Services.Migrations;

/// <summary>
/// Config migration v8 → v9: rewrites touch-button layer geometry from the fixed 90px
/// authoring tile into the owning device's own key size.
///
/// Layer geometry used to be authored and stored against a constant 90x90 tile no matter
/// which device the config belonged to, on the premise that the values had to stay
/// device-independent. They never did: every config file belongs to exactly one device,
/// scoped by slug and serial (see
/// <see cref="LoupixDeck.Utils.FileDialogHelper.GetConfigPath(DeviceRegistry.DeviceInfo, string)"/>),
/// so there is no shared file whose portability the fixed tile protected. Storing natively
/// removes the scaling step between the editor and the framebuffer.
///
/// Existing values are multiplied once by <c>KeySize / 90</c>, so a design keeps the
/// position and proportions it had. Every device that shipped before this change has a 90px
/// key, which makes the factor exactly 1 and this migration a no-op that only bumps the
/// version — the scaling branch exists for hand-copied files and for forward safety.
/// </summary>
public sealed class PerDeviceKeySizeMigrator : IConfigMigration
{
    /// <summary>The authoring tile every pre-v9 config was written against.</summary>
    private const double LegacyAuthoringTile = 90.0;

    public int FromVersion => 8;

    public void Apply(JObject root, string configFilePath)
    {
        DeviceRegistry.DeviceInfo device = TouchButtonLayerScaler.ResolveDevice(configFilePath);
        int keySize = device?.Geometry.KeySize ?? DeviceGeometry.Default.KeySize;

        // Every shipping device is 90px, so this is the path real configs take: the file
        // comes out byte-identical apart from the version.
        TouchButtonLayerScaler.ScaleAll(root, keySize / LegacyAuthoringTile);

        root["Version"] = FromVersion + 1;
    }
}
