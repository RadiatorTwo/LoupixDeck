namespace LoupixDeck.WaylandFocus.Tests;

/// <summary>
/// Compositor detection is environment-driven; the runtime directory is the one
/// input that can be present but unreadable, and both entry points must degrade
/// to "no compositor" instead of letting the IO error escape — DI registration
/// would otherwise take the whole app down at startup.
/// </summary>
public class CompositorDetectorTests
{
    [Fact]
    public void TryDetect_WhenRuntimeDirectoryIsMissing_ReturnsFalse()
    {
        using EnvScope scope = new(
            ("XDG_RUNTIME_DIR", "/nonexistent-loupixdeck-test"),
            ("NIRI_SOCKET", null),
            ("HYPRLAND_INSTANCE_SIGNATURE", null),
            ("SWAYSOCK", null));

        Assert.False(CompositorDetector.TryDetect(out string? name));
        Assert.Null(name);
    }

    [Fact]
    public void Detect_WhenRuntimeDirectoryIsMissing_ThrowsNotSupported()
    {
        using EnvScope scope = new(
            ("XDG_RUNTIME_DIR", "/nonexistent-loupixdeck-test"),
            ("NIRI_SOCKET", null),
            ("HYPRLAND_INSTANCE_SIGNATURE", null),
            ("SWAYSOCK", null));

        // The legacy contract holds: a detection problem surfaces as
        // NotSupportedException (which the monitor's degradation contract
        // handles), never as the underlying DirectoryNotFoundException.
        Assert.Throws<NotSupportedException>(() => CompositorDetector.Detect());
    }

    /// <summary>Sets the given variables for the test and restores them afterwards.</summary>
    private sealed class EnvScope : IDisposable
    {
        private readonly Dictionary<string, string?> _saved = new();

        public EnvScope(params (string Name, string? Value)[] changes)
        {
            foreach ((string name, string? value) in changes)
            {
                _saved[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        public void Dispose()
        {
            foreach ((string name, string? value) in _saved)
                Environment.SetEnvironmentVariable(name, value);
        }
    }
}