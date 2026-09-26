using LoupixDeck.Models;
using LoupixDeck.Registry;

namespace LoupixDeck.Services.Import.Lp5;

/// <summary>The control layout of the device a Loupedeck profile is converted for.</summary>
/// <param name="TouchButtonCount">Touch slots per page, including any side-strip slots.</param>
/// <param name="RotaryButtonCount">Dials on a shared (both-sides) rotary page.</param>
/// <param name="SideRotaryButtonCount">Dials on one side's rotary page.</param>
/// <param name="HasIndependentRotarySides">True when the device has left and right dial columns
/// with their own pages and side strips (Razer Stream Controller).</param>
/// <param name="Geometry">Pixel geometry, used to place icons and labels.</param>
public sealed record Lp5DeviceShape(
    int TouchButtonCount,
    int RotaryButtonCount,
    int SideRotaryButtonCount,
    bool HasIndependentRotarySides,
    DeviceGeometry Geometry);

/// <summary>Why a Loupedeck assignment could not be converted.</summary>
public enum Lp5UnsupportedReason
{
    /// <summary>An action with no LoupixDeck equivalent, typically a plugin action.</summary>
    UnknownAction,

    /// <summary>A profile action whose template is not supported (e.g. pointer moves).</summary>
    UnsupportedProfileAction,

    /// <summary>A macro containing a step that cannot be converted.</summary>
    UnsupportedMacro,

    /// <summary>The action points at a definition the profile does not contain.</summary>
    MissingDefinition,

    /// <summary>A page switch whose target page is not part of the workspace.</summary>
    PageOutsideWorkspace,

    /// <summary>A dial rotation with no LoupixDeck equivalent.</summary>
    UnsupportedAdjustment,

    /// <summary>Actions nested deeper than the converter follows.</summary>
    RecursionLimit
}

/// <summary>One control whose assignment was not (fully) converted.</summary>
/// <param name="Location">Where the control sits, e.g. "Home › Page 1 › Key 3".</param>
/// <param name="Label">The Loupedeck name of the action.</param>
/// <param name="Detail">The raw Loupedeck reference or template, for the curious.</param>
public sealed record Lp5UnsupportedControl(string Location, string Label, Lp5UnsupportedReason Reason, string Detail);

/// <summary>A remark about the conversion as a whole.</summary>
public enum Lp5NoteKind
{
    /// <summary>Loupedeck wheel pages (Loupedeck CT) have no equivalent and were skipped.</summary>
    WheelPagesSkipped,

    /// <summary>The source pages have more keys than the device; the surplus was dropped.</summary>
    SurplusKeys,

    /// <summary>The source pages have more dials than the device; the surplus was dropped.</summary>
    SurplusDials,

    /// <summary>Icons that could not be read were left out.</summary>
    UnreadableIcons
}

public sealed record Lp5Note(Lp5NoteKind Kind, int Count);

/// <summary>The outcome of converting a Loupedeck profile.</summary>
public sealed class Lp5ConversionResult
{
    public required Profile Profile { get; init; }

    public required IReadOnlyList<Lp5UnsupportedControl> Unsupported { get; init; }

    public required IReadOnlyList<Lp5Note> Notes { get; init; }

    public int Workspaces { get; init; }

    public int TouchPages { get; init; }

    public int RotaryPages { get; init; }

    /// <summary>Controls that carried an assignment in the source profile.</summary>
    public int TotalControls { get; init; }

    /// <summary>Controls whose assignment was converted, at least partly for dials.</summary>
    public int MappedControls { get; init; }

    /// <summary>Icons placed on keys and strips.</summary>
    public int Icons { get; init; }
}
