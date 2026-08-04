using Newtonsoft.Json.Linq;

namespace LoupixDeck.Services.Portable.Migrations;

/// <summary>
/// One step of the <c>.loupixprofile</c> envelope migration chain, modelled on the config's
/// <c>IConfigMigration</c>: it upgrades a package written with
/// <see cref="FromVersion"/> to <c>FromVersion + 1</c>.
/// </summary>
/// <remarks>
/// The chain ships empty at format version 1 — the interface and the loop that walks it exist
/// from the start so introducing version 2 is a purely additive change, exactly like adding a
/// config migrator is today.
/// </remarks>
public interface IPackageMigration
{
    /// <summary>Envelope version this migration upgrades from.</summary>
    int FromVersion { get; }

    /// <summary>
    /// Rewrites the raw manifest and payload documents in place. Working on the JSON rather than
    /// the typed models is what lets a migration reshape fields that no longer exist on the
    /// current model.
    /// </summary>
    void Apply(JObject manifest, JObject payload);
}
