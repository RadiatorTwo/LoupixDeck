using LoupixDeck.Registry;

namespace LoupixDeck.Utils;

/// <summary>
/// DEBUG-only escape hatch for testing a device port without owning the hardware:
/// set <c>LOUPIXDECK_FAKE_DEVICE</c> to a slug (e.g. <c>razer-stream-controller</c>
/// or <c>loupedeck-live-s</c>) and the app will pretend any connected supported
/// device is that type. Wire protocol is shared across Loupedeck-family devices,
/// so this lets you exercise the multi-device plumbing (per-device config file,
/// page sizes, button counts, UI grid) against a Live S; on-screen rendering will
/// be off because layout offsets and column counts differ.
/// </summary>
public static class FakeDeviceOverride
{
    private const string EnvVar = "LOUPIXDECK_FAKE_DEVICE";

    /// <summary>
    /// Returns the resolved device with its type swapped when the env var is set to
    /// a known slug, otherwise returns <paramref name="actual"/> unchanged. Only the
    /// device <em>type</em> is coerced — the real serial flows through, so per-device
    /// config scoping stays stable. Release builds are a no-op (this whole class is
    /// only compiled in for #if DEBUG callers).
    /// </summary>
    public static ResolvedDevice Apply(ResolvedDevice actual)
    {
        var slug = Environment.GetEnvironmentVariable(EnvVar);
        if (string.IsNullOrWhiteSpace(slug)) return actual;

        var match = DeviceRegistry.SupportedDevices
            .FirstOrDefault(d => string.Equals(d.Slug, slug.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match == null)
        {
            Console.WriteLine($"[FakeDeviceOverride] {EnvVar}='{slug}' did not match any registered device; ignoring.");
            return actual;
        }

        if (actual != null && actual.Info.Slug == match.Slug) return actual;

        Console.WriteLine($"[FakeDeviceOverride] Pretending the connected device is '{match.Name}' (was: {actual?.Info.Name ?? "<unresolved>"}).");
        return new ResolvedDevice(match, actual?.Serial);
    }
}
