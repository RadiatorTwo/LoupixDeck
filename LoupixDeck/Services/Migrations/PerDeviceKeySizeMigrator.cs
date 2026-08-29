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
///
/// Only touch-button layers are rewritten. A side strip is its own surface
/// (strip width x panel height), never a key tile, so strip canvases are left alone.
/// </summary>
public sealed class PerDeviceKeySizeMigrator : IConfigMigration
{
    /// <summary>The authoring tile every pre-v9 config was written against.</summary>
    private const double LegacyAuthoringTile = 90.0;

    /// <summary>
    /// Absolute layer properties measured in tile space. Unitless ones (Scale, Rotation,
    /// Opacity, gradient angle) and the source-image crop rectangle are deliberately absent —
    /// they do not change with the tile.
    /// </summary>
    private static readonly string[] IntProperties =
    [
        "PositionX", "PositionY",          // LayerBase
        "TextSize",                        // TextLayer
        "ShadowOffsetX", "ShadowOffsetY"   // SymbolLayer
    ];

    /// <summary>Same, but stored as doubles — rounding them would lose precision.</summary>
    private static readonly string[] DoubleProperties =
    [
        "OutlineWidth", "ShadowBlur"       // SymbolLayer
    ];

    /// <summary>
    /// Box size in tile space. Kept apart from <see cref="IntProperties"/> because 0 is
    /// meaningful here: it means "fill the device's key" and must stay 0.
    /// </summary>
    private static readonly string[] OptionalBoxProperties =
    [
        "BoxWidth", "BoxHeight"            // TextLayer
    ];

    public int FromVersion => 8;

    public void Apply(JObject root, string configFilePath)
    {
        double factor = ResolveKeySize(configFilePath) / LegacyAuthoringTile;

        // Every shipping device is 90px, so this is the path real configs take: the file
        // comes out byte-identical apart from the version.
        if (Math.Abs(factor - 1.0) > 1e-9)
        {
            foreach (JObject layer in EnumerateTouchButtonLayers(root))
                ScaleLayer(layer, factor);
        }

        root["Version"] = FromVersion + 1;
    }

    /// <summary>
    /// Walks the v8 shape: Profiles → Workspaces → TouchButtonPages → TouchButtons → States
    /// → Layers. Rotary pages (and the strip canvas hanging off them) are skipped on purpose.
    /// </summary>
    private static IEnumerable<JObject> EnumerateTouchButtonLayers(JObject root)
    {
        foreach (JObject profile in ChildObjects(root, "Profiles"))
        foreach (JObject workspace in ChildObjects(profile, "Workspaces"))
        foreach (JObject page in ChildObjects(workspace, "TouchButtonPages"))
        foreach (JObject button in ChildObjects(page, "TouchButtons"))
        foreach (JObject state in ChildObjects(button, "States"))
        foreach (JObject layer in ChildObjects(state, "Layers"))
            yield return layer;
    }

    private static IEnumerable<JObject> ChildObjects(JObject parent, string arrayName) =>
        parent[arrayName] is JArray array ? array.OfType<JObject>() : [];

    private static void ScaleLayer(JObject layer, double factor)
    {
        foreach (string name in IntProperties)
            ScaleInt(layer, name, factor);

        foreach (string name in OptionalBoxProperties)
            ScaleOptionalBox(layer, name, factor);

        foreach (string name in DoubleProperties)
            ScaleDouble(layer, name, factor);
    }

    private static void ScaleInt(JObject layer, string name, double factor)
    {
        if (!TryReadNumber(layer, name, out double value)) return;
        layer[name] = (int)Math.Round(value * factor, MidpointRounding.AwayFromZero);
    }

    private static void ScaleOptionalBox(JObject layer, string name, double factor)
    {
        if (!TryReadNumber(layer, name, out double value)) return;
        if (value <= 0) return; // 0 = "fill the device's key" — not a measurement.
        layer[name] = (int)Math.Round(value * factor, MidpointRounding.AwayFromZero);
    }

    private static void ScaleDouble(JObject layer, string name, double factor)
    {
        if (!TryReadNumber(layer, name, out double value)) return;
        layer[name] = value * factor;
    }

    private static bool TryReadNumber(JObject layer, string name, out double value)
    {
        value = 0;
        JToken token = layer[name];
        if (token == null || token.Type is not (JTokenType.Integer or JTokenType.Float)) return false;
        value = token.Value<double>();
        return true;
    }

    /// <summary>
    /// Derives the owning device's key size from the config file name
    /// (<c>config_&lt;slug&gt;[_&lt;serial&gt;].json</c>). The longest matching slug wins so
    /// "razer-stream-controller-x" is not shadowed by "razer-stream-controller". Falls back
    /// to the default geometry when the name matches nothing, which keeps the factor at 1.
    /// </summary>
    private static int ResolveKeySize(string configFilePath)
    {
        string name = Path.GetFileNameWithoutExtension(configFilePath ?? string.Empty);
        if (!name.StartsWith("config_", StringComparison.OrdinalIgnoreCase))
            return DeviceGeometry.Default.KeySize;

        string remainder = name["config_".Length..];

        DeviceRegistry.DeviceInfo match = DeviceRegistry.SupportedDevices
            .Where(d => remainder.StartsWith(d.Slug, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => d.Slug.Length)
            .FirstOrDefault();

        return match?.Geometry.KeySize ?? DeviceGeometry.Default.KeySize;
    }
}
