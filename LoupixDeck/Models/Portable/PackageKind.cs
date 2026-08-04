namespace LoupixDeck.Models.Portable;

/// <summary>
/// What a <c>.loupixprofile</c> package contains. Serialized as a string in the manifest so an
/// unknown future kind surfaces as a readable error instead of silently deserializing to
/// <see cref="Profile"/> (the default numeric value).
/// </summary>
public enum PackageKind
{
    /// <summary>A whole <see cref="Models.Profile"/> with all of its workspaces.</summary>
    Profile,

    /// <summary>A single <see cref="Models.Workspace"/> with all of its pages.</summary>
    Workspace,

    /// <summary>A single <see cref="TouchButtonPage"/>.</summary>
    TouchPage,

    /// <summary>A single <see cref="RotaryButtonPage"/>.</summary>
    RotaryPage
}
