using Newtonsoft.Json.Linq;

namespace LoupixDeck.Services.Portable;

/// <summary>
/// Rewrites the identities inside an imported payload so it can live next to the item it was
/// exported from. Works on the raw JSON, before deserialization, which is what makes it
/// schema-agnostic.
/// </summary>
/// <remarks>
/// The whole-document string substitution is the point of this class, not an optimization. Ids are
/// not confined to id-shaped properties: <c>System.ActivateProfile(&lt;guid&gt;)</c> and
/// <c>System.GotoWorkspace(&lt;guid&gt;)</c> embed raw GUIDs inside command strings, which also reach
/// <c>CommandWrap.PreCommands</c>/<c>PostCommands</c>, <c>RotaryButtonPage.StripSegmentCommands</c>
/// and the <c>OwnerKey</c> of plugin/text layers derived from those commands. A remapper that only
/// touched model properties would produce an imported copy whose internal navigation buttons still
/// jump to the original profile — silently, and only noticeable at runtime.
/// </remarks>
public static class PortableIdRemapper
{
    /// <summary>
    /// JSON property names holding a structural identity that must become unique on import.
    /// <c>Id</c> covers profiles, workspaces, button states and layers alike — every reference to
    /// them (<c>HomeWorkspaceId</c>, <c>DefaultStateId</c>, <c>ActiveStateId</c>,
    /// <c>TargetStateId</c>, command arguments) is caught by the substitution pass instead.
    /// </summary>
    private const string IdPropertyName = "Id";

    /// <summary>
    /// Assigns a fresh id to every structural id in <paramref name="payload"/> and replaces every
    /// occurrence of the old ids anywhere in the document.
    /// </summary>
    /// <param name="payload">The payload document; rewritten in place and also returned.</param>
    /// <param name="seed">
    /// Pre-assigned mappings. "Replace" mode seeds the incoming profile/workspace id to the id of
    /// the item being replaced, so everything already pointing at the target (active/startup
    /// profile, context rules) keeps resolving.
    /// </param>
    /// <returns>The rewritten payload and the full old → new map.</returns>
    public static (JObject Payload, IReadOnlyDictionary<Guid, Guid> Map) Remap(
        JObject payload, IReadOnlyDictionary<Guid, Guid> seed = null)
    {
        ArgumentNullException.ThrowIfNull(payload);

        Dictionary<Guid, Guid> map = seed != null ? new Dictionary<Guid, Guid>(seed) : [];

        foreach (JProperty property in payload.DescendantsAndSelf().OfType<JProperty>())
        {
            if (!string.Equals(property.Name, IdPropertyName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (TryReadGuid(property.Value, out Guid id) && id != Guid.Empty && !map.ContainsKey(id))
                map[id] = Guid.NewGuid();
        }

        if (map.Count > 0)
            SubstituteGuids(payload, map);

        return (payload, map);
    }

    /// <summary>
    /// Replaces every string in the document that references one of the mapped ids: exact matches
    /// (id properties, guid-typed values) as well as ids embedded in a longer string (command
    /// arguments, layer owner keys).
    /// </summary>
    public static void SubstituteGuids(JToken root, IReadOnlyDictionary<Guid, Guid> map)
    {
        if (root is not JContainer container || map == null || map.Count == 0)
            return;

        // "D" format ("xxxxxxxx-xxxx-…") is what both Newtonsoft and Guid.ToString() produce and
        // what the profile/workspace commands embed.
        Dictionary<string, string> textual = map.ToDictionary(
            pair => pair.Key.ToString("D"),
            pair => pair.Value.ToString("D"),
            StringComparer.OrdinalIgnoreCase);

        foreach (JValue value in container.DescendantsAndSelf().OfType<JValue>().ToList())
        {
            switch (value.Type)
            {
                case JTokenType.Guid when value.Value is Guid guid && map.TryGetValue(guid, out Guid mappedGuid):
                    value.Value = mappedGuid;
                    break;

                case JTokenType.String when value.Value is string text && text.Length > 0:
                    string replaced = ReplaceAll(text, textual);
                    if (!ReferenceEquals(replaced, text))
                        value.Value = replaced;
                    break;
            }
        }
    }

    /// <summary>
    /// Replaces every occurrence of the given substrings, case-insensitively. Returns the original
    /// reference when nothing matched so callers can skip the write.
    /// </summary>
    public static string ReplaceAll(string text, IReadOnlyDictionary<string, string> replacements)
    {
        if (string.IsNullOrEmpty(text) || replacements == null || replacements.Count == 0)
            return text;

        string result = text;

        foreach (KeyValuePair<string, string> replacement in replacements)
        {
            if (result.Contains(replacement.Key, StringComparison.OrdinalIgnoreCase))
                result = result.Replace(replacement.Key, replacement.Value, StringComparison.OrdinalIgnoreCase);
        }

        return ReferenceEquals(result, text) ? text : result;
    }

    private static bool TryReadGuid(JToken token, out Guid value)
    {
        switch (token)
        {
            case JValue { Type: JTokenType.Guid } guidValue when guidValue.Value is Guid guid:
                value = guid;
                return true;

            case JValue { Type: JTokenType.String } stringValue:
                return Guid.TryParse((string)stringValue.Value, out value);

            default:
                value = Guid.Empty;
                return false;
        }
    }
}
