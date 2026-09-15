using Newtonsoft.Json;

namespace LoupixDeck.Models.Companion;

/// <summary>
/// The pages a master wants one of its companions to show, inside the workspace they share.
/// Pages are referenced by their stable id, which is unique to the companion's own config.
/// Every page is optional; an id that does not resolve in the companion's active workspace is skipped.
/// </summary>
public sealed class CompanionPageTarget
{
    /// <summary>Scope key of the companion.</summary>
    public string DeviceKey { get; set; } = string.Empty;

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public Guid? TouchPageId { get; set; }

    /// <summary>Rotary page for both dial columns.</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public Guid? RotaryPageId { get; set; }

    /// <summary>Left dial-column page on a side-strip device.</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public Guid? LeftRotaryPageId { get; set; }

    /// <summary>Right dial-column page on a side-strip device.</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public Guid? RightRotaryPageId { get; set; }
}
