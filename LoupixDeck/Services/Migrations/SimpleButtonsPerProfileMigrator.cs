using Newtonsoft.Json.Linq;

namespace LoupixDeck.Services.Migrations;

/// <summary>
/// Config migration v11 → v12: the round LED buttons become per-profile.
///
/// <c>SimpleButtons</c> used to be a single array at the config root, shared by every profile, so a
/// colour or command changed in one profile showed up in all of them. The array now lives inside
/// each <c>Profile</c>. To keep an old file behaving exactly as before, the shared set is
/// deep-cloned into every existing profile — each profile starts from precisely what the device
/// displayed — and the root key is removed.
///
/// A profile that already carries its own array (a partially upgraded or hand-edited file) is left
/// alone, and a file without a usable root array leaves every profile untouched: null means "build
/// the device defaults on first activation", which is what a config that never completed device
/// bring-up needs. Running the migration twice therefore changes nothing the second time.
/// </summary>
public sealed class SimpleButtonsPerProfileMigrator : IConfigMigration
{
    public int FromVersion => 11;

    public void Apply(JObject root, string configFilePath)
    {
        if (root["SimpleButtons"] is JArray shared && root["Profiles"] is JArray profiles)
        {
            foreach (JObject profile in profiles.OfType<JObject>())
            {
                // Only fill in a profile that has none; never overwrite an existing set.
                if (profile["SimpleButtons"] is JArray existing && existing.Count > 0) continue;

                // Deep clone: the profiles must not end up sharing one JSON node, which would
                // deserialize into a single array instance again.
                profile["SimpleButtons"] = (JArray)shared.DeepClone();
            }
        }

        // The root key is gone in v12 whether or not it held anything usable — the property no
        // longer serializes, so leaving it behind would only rot in the file.
        root.Remove("SimpleButtons");

        root["Version"] = FromVersion + 1;
    }
}
