using Newtonsoft.Json.Linq;

namespace LoupixDeck.Services.Migrations;

/// <summary>
/// One step in the config upgrade chain. A migration upgrades a config from
/// <see cref="FromVersion"/> to <c>FromVersion + 1</c>. <see cref="ConfigService"/>
/// applies the steps in sequence until the config reaches the current version.
/// </summary>
public interface IConfigMigration
{
    /// <summary>The config schema version this migration upgrades from.</summary>
    int FromVersion { get; }

    /// <summary>
    /// Upgrades <paramref name="root"/> in place. Implementations MUST set
    /// <c>root["Version"]</c> to <see cref="FromVersion"/> + 1.
    /// </summary>
    /// <param name="root">The parsed config JSON to mutate.</param>
    /// <param name="configFilePath">Path of the config file being migrated —
    /// lets a migration locate sibling files (e.g. legacy integration configs).</param>
    void Apply(JObject root, string configFilePath);
}
