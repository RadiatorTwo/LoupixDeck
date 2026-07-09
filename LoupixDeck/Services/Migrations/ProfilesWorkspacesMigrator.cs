using Newtonsoft.Json.Linq;

namespace LoupixDeck.Services.Migrations;

/// <summary>
/// Config migration v7 → v8: introduces profiles and workspaces (issue #132). The touch/rotary
/// page collections and <c>StartupTouchPageIndex</c> used to live directly on the config root.
/// They move, unchanged, into a single <c>Home</c> workspace inside a single <c>Default</c>
/// profile; the root gains <c>Profiles</c> plus the active/startup profile ids. Because the old
/// config had exactly one implicit page set, the migrated single-profile/single-workspace layout
/// behaves identically. <c>AppPageBindings</c> are intentionally left untouched — their page
/// indices now resolve within the one default workspace, so app-switching keeps working as before
/// (the new profile/workspace-aware rule engine folds them in at runtime in a later phase).
/// </summary>
public sealed class ProfilesWorkspacesMigrator : IConfigMigration
{
    public int FromVersion => 7;

    // Page collections that move from the config root into the Home workspace.
    private static readonly string[] PageKeys =
    [
        "TouchButtonPages",
        "RotaryButtonPages",
        "LeftRotaryButtonPages",
        "RightRotaryButtonPages"
    ];

    public void Apply(JObject root, string configFilePath)
    {
        // Defensive: a partially upgraded file that already has profiles must not be re-wrapped.
        if (root["Profiles"] is JArray existing && existing.Count > 0)
        {
            root["Version"] = FromVersion + 1;
            return;
        }

        var workspaceId = Guid.NewGuid();
        var profileId = Guid.NewGuid();

        var workspace = new JObject
        {
            ["Id"] = workspaceId,
            ["Name"] = "Home",
            // Carry the former device-wide startup page over as this workspace's startup page.
            ["StartupTouchPageIndex"] = root["StartupTouchPageIndex"] ?? 0
        };

        // Move whichever page collections exist; default any missing one to an empty array so the
        // deserialized workspace has the same non-null collections a fresh one would.
        foreach (var key in PageKeys)
            workspace[key] = root[key] ?? new JArray();

        var profile = new JObject
        {
            ["Id"] = profileId,
            ["Name"] = "Default",
            ["HomeWorkspaceId"] = workspaceId,
            ["Priority"] = 0,
            ["Workspaces"] = new JArray(workspace)
        };

        root["Profiles"] = new JArray(profile);
        root["ActiveProfileId"] = profileId;
        root["StartupProfileId"] = profileId;

        // Remove the keys that no longer live at the root (now under the Home workspace).
        root.Remove("StartupTouchPageIndex");
        foreach (var key in PageKeys)
            root.Remove(key);

        root["Version"] = FromVersion + 1;
    }
}
