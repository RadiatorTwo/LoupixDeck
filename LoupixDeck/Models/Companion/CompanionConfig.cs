namespace LoupixDeck.Models.Companion;

/// <summary>
/// Root of <c>companions.json</c>: the global relationships between devices. Deliberately kept
/// out of the per-device <see cref="LoupedeckConfig"/> files — a relationship spans devices, and
/// storing it in every participant would let the copies disagree.
/// </summary>
public sealed class CompanionConfig
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public List<CompanionGroup> Groups { get; set; } = [];
}
