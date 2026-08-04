using Newtonsoft.Json.Linq;

namespace LoupixDeck.Utils;

/// <summary>
/// Schema-agnostic collection of asset-folder relative paths ("assets/…") out of arbitrary
/// JSON. Used by the save-time asset cleanup (which must see every profile's images, not just
/// the active one) and by the profile package exporter (which must ship exactly the assets the
/// exported subtree references). Both share this code so they can never disagree about what
/// counts as an asset reference.
/// </summary>
public static class AssetPathHarvester
{
    /// <summary>True when a stored string looks like an asset-folder relative path.</summary>
    public static bool IsAssetPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        string normalized = value.Replace('\\', '/').TrimStart('/');
        return normalized.StartsWith("assets/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns every string value anywhere below <paramref name="root"/> that looks like an
    /// asset-folder relative path. Deliberately schema-agnostic (any JSON value under the
    /// "assets/" prefix) so it stays correct for wallpapers, image layers, and any future asset
    /// reference — in any profile/workspace — without having to mirror the config schema.
    /// </summary>
    public static IEnumerable<string> Harvest(JToken root)
    {
        if (root is not JContainer container)
        {
            if (root is JValue single && single.Type == JTokenType.String && IsAssetPath((string)single.Value))
                yield return (string)single.Value;
            yield break;
        }

        foreach (JValue value in container.DescendantsAndSelf().OfType<JValue>())
        {
            if (value.Type != JTokenType.String) continue;

            string text = (string)value.Value;
            if (IsAssetPath(text))
                yield return text;
        }
    }

    /// <summary>
    /// Parses the JSON file at <paramref name="path"/> and returns every asset-relative path in it.
    /// A corrupt or unreadable file must not abort a cleanup run — but it also must not cause its
    /// assets to be deleted, so it is logged and reported as "nothing found".
    /// </summary>
    public static List<string> HarvestFromFile(string path)
    {
        try
        {
            return Harvest(JToken.Parse(File.ReadAllText(path))).ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Asset scan: skipping unreadable file '{path}': {ex.Message}");
            return [];
        }
    }
}
