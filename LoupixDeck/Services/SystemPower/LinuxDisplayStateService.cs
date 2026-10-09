using LoupixDeck.Services.SystemPower.Linux;

namespace LoupixDeck.Services.SystemPower;

/// <summary>
/// Linux has no single display-state API, so the first source that works wins, each of them
/// event-driven:
/// <list type="number">
/// <item>GNOME (X11 and Wayland): Mutter's <c>PowerSaveMode</c> property on the session bus.</item>
/// <item>Other Wayland compositors: the KDE DPMS or wlroots output-power protocol.</item>
/// <item>X11: the DPMS 1.2 <c>InfoNotify</c> event (Xorg 21.1+).</item>
/// </list>
/// The X11 DPMS state without that event can only be polled, which is deliberately not done; such
/// a session stays unsupported.
/// </summary>
public sealed class LinuxDisplayStateService : DisplayStateServiceBase, IDisposable
{
    private ILinuxDisplaySource _source;

    protected override void Start() => _ = Task.Run(AttachAsync);

    private async Task AttachAsync()
    {
        foreach (Func<ILinuxDisplaySource> create in Candidates())
        {
            ILinuxDisplaySource source = create();
            try
            {
                if (await source.TryStartAsync(Report))
                {
                    _source = source;
                    MarkSupported();
                    Console.WriteLine($"[Displays] Following the display state through {source.Name}.");
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Displays] {source.Name} unavailable: {ex.Message}");
            }

            source.Dispose();
        }

        Console.WriteLine("[Displays] No display state source found for this session.");
    }

    private static IEnumerable<Func<ILinuxDisplaySource>> Candidates()
    {
        yield return () => new MutterDisplaySource();
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
            yield return () => new WaylandDpmsSource();
        else if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
            yield return () => new X11DpmsSource();
    }

    public void Dispose()
    {
        _source?.Dispose();
        _source = null;
    }
}
