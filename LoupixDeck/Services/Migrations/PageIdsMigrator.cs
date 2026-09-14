using Newtonsoft.Json.Linq;

namespace LoupixDeck.Services.Migrations;

/// <summary>
/// Config migration v12 → v13: button pages get a stable <c>Id</c>.
///
/// Every touch and rotary page in every workspace (including the left/right rotary sets of
/// side-strip devices) receives a fresh GUID unless it already carries one. Without this step a
/// page's id would be regenerated on every load until the file happened to be saved, so nothing
/// could reference it reliably.
///
/// Context rules that jump to a page by index are then resolved to that page's id — but only when
/// the rule names the workspace the index applies to: its <c>ActivateWorkspaceId</c>, or the home
/// workspace of its <c>ActivateProfileId</c>. A rule without either jumps inside whatever workspace
/// is active at runtime, so its index has no single page to resolve to. The index is always kept:
/// an unresolved rule behaves exactly as before, and a resolved one only differs once pages are
/// reordered — which is the point of the ids. Running the migration twice changes nothing.
/// </summary>
public sealed class PageIdsMigrator : IConfigMigration
{
    public int FromVersion => 12;

    private static readonly string[] PageCollections =
        ["TouchButtonPages", "RotaryButtonPages", "LeftRotaryButtonPages", "RightRotaryButtonPages"];

    public void Apply(JObject root, string configFilePath)
    {
        JArray profiles = root["Profiles"] as JArray ?? [];

        foreach (JObject workspace in profiles.OfType<JObject>().SelectMany(Workspaces))
        foreach (string collection in PageCollections)
        foreach (JObject page in (workspace[collection] as JArray ?? []).OfType<JObject>())
        {
            if (!TryReadGuid(page["Id"], out _))
                page["Id"] = Guid.NewGuid().ToString();
        }

        foreach (JObject rule in (root["ContextRules"] as JArray ?? []).OfType<JObject>())
        {
            JObject workspace = TargetWorkspace(rule, profiles);
            if (workspace == null) continue;

            ResolvePage(rule, "TouchPageIndex", "TouchPageId", workspace["TouchButtonPages"] as JArray);
            ResolvePage(rule, "RotaryPageIndex", "RotaryPageId", workspace["RotaryButtonPages"] as JArray);
        }

        root["Version"] = FromVersion + 1;
    }

    private static IEnumerable<JObject> Workspaces(JObject profile) =>
        (profile["Workspaces"] as JArray ?? []).OfType<JObject>();

    /// <summary>The workspace a rule's page index applies to, mirroring the runtime order: the
    /// rule's workspace wins, else the home workspace of its profile. Null when neither is set or
    /// resolves.</summary>
    private static JObject TargetWorkspace(JObject rule, JArray profiles)
    {
        List<JObject> allProfiles = profiles.OfType<JObject>().ToList();

        if (TryReadGuid(rule["ActivateWorkspaceId"], out Guid workspaceId))
        {
            JObject match = allProfiles.SelectMany(Workspaces)
                .FirstOrDefault(w => TryReadGuid(w["Id"], out Guid id) && id == workspaceId);
            if (match != null) return match;
        }

        if (!TryReadGuid(rule["ActivateProfileId"], out Guid profileId)) return null;

        JObject profile = allProfiles.FirstOrDefault(p => TryReadGuid(p["Id"], out Guid id) && id == profileId);
        if (profile == null) return null;

        List<JObject> workspaces = Workspaces(profile).ToList();
        return (TryReadGuid(profile["HomeWorkspaceId"], out Guid homeId)
                   ? workspaces.FirstOrDefault(w => TryReadGuid(w["Id"], out Guid id) && id == homeId)
                   : null)
               ?? workspaces.FirstOrDefault();
    }

    private static void ResolvePage(JObject rule, string indexName, string idName, JArray pages)
    {
        if (TryReadGuid(rule[idName], out _)) return;
        if (rule[indexName] is not JValue { Type: JTokenType.Integer } indexValue) return;

        int index = indexValue.Value<int>();
        if (pages == null || index < 0 || index >= pages.Count) return;

        if (pages[index] is JObject page && TryReadGuid(page["Id"], out Guid pageId))
            rule[idName] = pageId.ToString();
    }

    private static bool TryReadGuid(JToken token, out Guid value)
    {
        value = Guid.Empty;
        return token switch
        {
            JValue { Type: JTokenType.Guid } guidValue when guidValue.Value is Guid guid => (value = guid) != Guid.Empty,
            JValue { Type: JTokenType.String } stringValue => Guid.TryParse((string)stringValue.Value, out value) && value != Guid.Empty,
            _ => false
        };
    }
}
