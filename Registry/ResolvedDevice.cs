using LoupixDeck.Utils;

namespace LoupixDeck.Registry;

/// <summary>
/// A concrete physical device instance: a device <em>type</em>
/// (<see cref="DeviceRegistry.DeviceInfo"/>) paired with its normalized USB serial.
///
/// Kept separate from <see cref="DeviceRegistry.DeviceInfo"/> on purpose —
/// <c>DeviceInfo</c> is a value-compared type catalog entry (DI singleton, registry
/// key). The serial lives here so two physically identical units stay distinct
/// without polluting the type catalog. <see cref="Serial"/> may be null for devices
/// without a real iSerial; everything then falls back to the slug-only scope.
/// </summary>
public sealed record ResolvedDevice(DeviceRegistry.DeviceInfo Info, string Serial)
{
    public string Slug => Info.Slug;

    /// <summary>
    /// Filesystem-safe scoping token: <c>slug</c> when there is no usable serial,
    /// else <c>slug_&lt;serial&gt;</c>. Identifies one physical unit's config + marker.
    /// </summary>
    public string ScopeKey
    {
        get
        {
            var safe = SerialNormalizer.ForFilename(Serial);
            return string.IsNullOrEmpty(safe) ? Info.Slug : $"{Info.Slug}_{safe}";
        }
    }
}
