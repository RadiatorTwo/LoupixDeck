using LoupixDeck.Registry;
using Newtonsoft.Json.Linq;

namespace LoupixDeck.Services.Migrations;

/// <summary>
/// Rescales touch-button layer geometry in a raw config document. Shared by the migrations
/// that move a config from one key-tile size to another, so the set of properties that count
/// as measurements — and the rules for the awkward ones — is stated once.
///
/// Only touch-button layers are rewritten. A side strip is its own surface
/// (strip width x panel height), never a key tile, so strip canvases are left alone.
/// </summary>
internal static class TouchButtonLayerScaler
{
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

    /// <summary>
    /// Multiplies every measured layer property by <paramref name="factor"/>. A factor of 1
    /// leaves the document untouched, so a caller can hand one over unconditionally.
    /// </summary>
    public static void ScaleAll(JObject root, double factor)
    {
        if (root == null || Math.Abs(factor - 1.0) <= 1e-9) return;

        foreach (JObject layer in EnumerateTouchButtonLayers(root))
        {
            foreach (string name in IntProperties)
                ScaleInt(layer, name, factor);

            foreach (string name in OptionalBoxProperties)
                ScaleOptionalBox(layer, name, factor);

            foreach (string name in DoubleProperties)
                ScaleDouble(layer, name, factor);
        }
    }

    /// <summary>
    /// Walks the document shape: Profiles → Workspaces → TouchButtonPages → TouchButtons →
    /// States → Layers. Rotary pages (and the strip canvas hanging off them) are skipped on
    /// purpose.
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
    /// The device a config file belongs to, derived from its name
    /// (<c>config_&lt;slug&gt;[_&lt;serial&gt;].json</c>). The longest matching slug wins so
    /// "razer-stream-controller-x" is not shadowed by "razer-stream-controller". Null when
    /// the name matches nothing.
    /// </summary>
    public static DeviceRegistry.DeviceInfo ResolveDevice(string configFilePath)
    {
        string name = Path.GetFileNameWithoutExtension(configFilePath ?? string.Empty);
        if (!name.StartsWith("config_", StringComparison.OrdinalIgnoreCase))
            return null;

        string remainder = name["config_".Length..];

        return DeviceRegistry.SupportedDevices
            .Where(d => remainder.StartsWith(d.Slug, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => d.Slug.Length)
            .FirstOrDefault();
    }
}
