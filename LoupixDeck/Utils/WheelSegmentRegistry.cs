using System.Collections.Concurrent;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Utils;

/// <summary>
/// Maps the runtime key of a split wheel's segment (<see cref="Models.WheelSegment.RuntimeKey"/>)
/// to the segment it describes, so the plugin bridge can fill <see cref="CommandContext.WheelSegment"/>
/// from the button key it already receives on every press and render call. Keys are unique per
/// session and never reused, so an entry left by a deleted wheel mode is never matched again.
/// </summary>
public static class WheelSegmentRegistry
{
    private static readonly ConcurrentDictionary<string, WheelSegmentInfo> Segments = new();

    /// <summary>Records that <paramref name="key"/> belongs to the segment <paramref name="info"/>.</summary>
    public static void Set(string key, WheelSegmentInfo info)
    {
        if (!string.IsNullOrEmpty(key)) Segments[key] = info;
    }

    /// <summary>Forgets <paramref name="key"/>, e.g. when its segment is hidden by a mode with fewer segments.</summary>
    public static void Remove(string key)
    {
        if (!string.IsNullOrEmpty(key)) Segments.TryRemove(key, out _);
    }

    /// <summary>The segment <paramref name="buttonKey"/> belongs to, or null for any other button.</summary>
    public static WheelSegmentInfo? Find(string buttonKey) =>
        buttonKey != null && Segments.TryGetValue(buttonKey, out var info) ? info : null;
}
