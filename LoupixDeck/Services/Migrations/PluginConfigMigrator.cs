using Newtonsoft.Json.Linq;

namespace LoupixDeck.Services.Migrations;

/// <summary>
/// Config migration v2 → v3: introduced when the integrations became plugins.
/// </summary>
/// <remarks>
/// <para>
/// It seeds the <c>EnabledPlugins</c> id list so the user's previous setup is
/// preserved: integrations that had a master switch (Elgato, Argus, HWiNFO) are
/// enabled only when their flag was on; integrations that were always active
/// (OBS, CoolerControl, Windows audio) are enabled unconditionally.
/// </para>
/// <para>
/// Integration-specific connection settings (OBS, Elgato, CoolerControl URL) are
/// intentionally NOT moved here — each integration plugin migrates its own
/// legacy config on first run, so the core never needs a plugin's settings schema.
/// </para>
/// </remarks>
public sealed class PluginConfigMigrator : IConfigMigration
{
    public int FromVersion => 2;

    // Integrations that had no opt-in flag — they were always active in v2.
    private static readonly string[] AlwaysEnabled = ["obs", "coolercontrol", "audio"];

    public void Apply(JObject root, string configFilePath)
    {
        var enabled = root["EnabledPlugins"] as JArray ?? new JArray();

        foreach (var id in AlwaysEnabled)
            Add(enabled, id);

        // The legacy master switches map to their integration plugin ids.
        AddIfFlag(root, enabled, "ElgatoEnabled", "elgato");
        AddIfFlag(root, enabled, "ArgusMonitorEnabled", "argus");
        AddIfFlag(root, enabled, "HwInfoEnabled", "hwinfo");

        root["EnabledPlugins"] = enabled;
        root["Version"] = FromVersion + 1;
    }

    private static void AddIfFlag(JObject root, JArray list, string flag, string pluginId)
    {
        if (root[flag]?.Value<bool?>() == true)
            Add(list, pluginId);
    }

    private static void Add(JArray list, string pluginId)
    {
        if (list.Any(t => string.Equals(t.Value<string>(), pluginId, StringComparison.OrdinalIgnoreCase)))
            return;

        list.Add(pluginId);
    }
}
