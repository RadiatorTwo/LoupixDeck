using Newtonsoft.Json;

namespace LoupixDeck.Models.Portable;

/// <summary>
/// <c>payload.json</c> of a <c>.loupixprofile</c> package: the exported subtree. Exactly one
/// property is populated; <see cref="ProfilePackageManifest.Kind"/> is the discriminator.
/// </summary>
/// <remarks>
/// Serialized and deserialized through <c>IConfigService</c>, which for any type other than
/// <c>LoupedeckConfig</c> is a pure Newtonsoft round-trip with the app's converter set
/// (<c>ColorJsonConverter</c>, <c>LayerJsonConverter</c> for the polymorphic layers,
/// <c>SKBitmapBase64Converter</c>). Using the same vehicle as the config is what guarantees a
/// package round-trips byte-for-byte identically to how the item is stored locally — there is
/// deliberately no second <c>JsonSerializerSettings</c> for packages.
/// </remarks>
public sealed class ProfilePackagePayload
{
    /// <summary>Populated when <see cref="ProfilePackageManifest.Kind"/> is <see cref="PackageKind.Profile"/>.</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public Profile Profile { get; set; }

    /// <summary>Populated when <see cref="ProfilePackageManifest.Kind"/> is <see cref="PackageKind.Workspace"/>.</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public Workspace Workspace { get; set; }

    /// <summary>Populated when <see cref="ProfilePackageManifest.Kind"/> is <see cref="PackageKind.TouchPage"/>.</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public TouchButtonPage TouchPage { get; set; }

    /// <summary>Populated when <see cref="ProfilePackageManifest.Kind"/> is <see cref="PackageKind.RotaryPage"/>.</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public RotaryButtonPage RotaryPage { get; set; }
}
