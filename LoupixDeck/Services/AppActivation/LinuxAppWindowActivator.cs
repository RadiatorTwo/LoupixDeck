using System.Runtime.Versioning;
using LoupixDeck.Services.AppSwitching;
using LoupixDeck.Services.Diagnostics.Linux;
using LoupixDeck.WaylandFocus;
using LoupixDeck.WaylandFocus.Activation;

namespace LoupixDeck.Services.AppActivation;

/// <summary>
/// Linux implementation. Picks the same mechanism page switching by application uses: the
/// compositor's IPC on COSMIC, sway, niri and Hyprland, KWin scripting on Plasma Wayland, the
/// "Activate Window By Title" extension on GNOME Wayland when it is installed, and X11
/// elsewhere — on a Wayland session without a supported compositor that is XWayland, which
/// reaches only X11 windows.
/// </summary>
/// <remarks>
/// A window counts as the application's when its process (from the compositor's PID, or from
/// the app_id where the compositor withholds PIDs) carries the name, when it belongs to a
/// process <see cref="ProcessNamer.FindByAppId"/> resolves from the name, or when its app_id
/// or window class is the name itself — the form a Flatpak id such as
/// <c>com.brave.Browser</c> takes.
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class LinuxAppWindowActivator : IAppWindowActivator
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly Lazy<Backend> _backend = new(SelectBackend);

    private enum Kind
    {
        /// <summary>Sees every window of the session.</summary>
        Complete,

        /// <summary>X11 on a Wayland session: blind to native Wayland windows.</summary>
        XWaylandOnly,

        KWin,

        /// <summary>
        /// GNOME Wayland: the Shell extension when it runs, otherwise X11 as
        /// <see cref="XWaylandOnly"/>. Checked on every press, so enabling the extension from
        /// the diagnostics takes effect without a restart.
        /// </summary>
        Gnome,

        None
    }

    private sealed record Backend(Kind Kind, IWindowActivator Activator);

    public async Task<AppActivationOutcome> ActivateAsync(string processName, IReadOnlyCollection<string> windowClasses,
        CancellationToken cancellationToken)
    {
        Backend backend = _backend.Value;
        string name = processName.Trim();

        // Off the caller's thread: the /proc scan reads every process.
        HashSet<int> processIds = await Task.Run(
            () => ProcessNamer.FindByAppId(name).Select(match => match.Pid).ToHashSet(), cancellationToken);

        if (backend.Kind == Kind.None)
            return processIds.Count == 0 ? AppActivationOutcome.NotRunning : AppActivationOutcome.Unsupported;

        try
        {
            return await ActivateAsync(backend, name, windowClasses, processIds, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // A wedged or stale IPC socket must not turn the button into a second-instance
            // launcher: only an application that is not running at all is started.
            Console.WriteLine($"[AppActivation] {backend.Kind} focus failed: {ex.Message}");
            return processIds.Count == 0 ? AppActivationOutcome.NotRunning : AppActivationOutcome.Unsupported;
        }
    }

    private static async Task<AppActivationOutcome> ActivateAsync(Backend backend, string name,
        IReadOnlyCollection<string> windowClasses, HashSet<int> processIds, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        if (backend.Kind == Kind.Gnome)
        {
            if (processIds.Count == 0)
                return AppActivationOutcome.NotRunning;

            // The process name first: it is what the user typed or what the target resolved to.
            List<string> candidates = [name, .. windowClasses.Where(c => !string.IsNullOrWhiteSpace(c))];
            switch (await GnomeWindowActivator.ActivateAsync(
                        candidates.Distinct(StringComparer.Ordinal).ToList(), timeout.Token))
            {
                case GnomeWindowActivator.Result.Activated:
                    return AppActivationOutcome.Activated;
                case GnomeWindowActivator.Result.NotFound:
                    // Mutter sees every window, but matches the class exactly: a running process
                    // without a match may simply carry a class nobody named. Starting it again
                    // could open a second instance, so nothing is done.
                    return AppActivationOutcome.Unsupported;
            }

            // No extension: XWayland windows are still reachable through X11, when there is one.
            if (backend.Activator == null)
                return AppActivationOutcome.Unsupported;

            backend = backend with { Kind = Kind.XWaylandOnly };
        }

        if (backend.Kind == Kind.KWin)
        {
            if (processIds.Count == 0)
                return AppActivationOutcome.NotRunning;

            await new KWinWindowActivator().ActivateAsync(name, processIds, timeout.Token);
            return AppActivationOutcome.Activated;
        }

        WindowActivationResult result = await backend.Activator.ActivateAsync(
            window => Matches(window, name, processIds), timeout.Token);

        return result switch
        {
            WindowActivationResult.AlreadyFocused => AppActivationOutcome.AlreadyFocused,
            WindowActivationResult.Activated => AppActivationOutcome.Activated,
            // A complete list without a match means no window: start it, the running instance
            // (if any, e.g. in the tray) takes over. XWayland cannot see native windows, so a
            // running process there may well have one that simply cannot be reached.
            _ when processIds.Count == 0 => AppActivationOutcome.NotRunning,
            _ when backend.Kind == Kind.Complete => AppActivationOutcome.NoWindow,
            _ => AppActivationOutcome.Unsupported
        };
    }

    private static bool Matches(ToplevelWindow window, string name, HashSet<int> processIds)
    {
        if (window.Pid is { } pid && processIds.Contains(pid))
            return true;

        if (string.Equals(window.AppId, name, StringComparison.OrdinalIgnoreCase))
            return true;

        string processName = ProcessNamer.Enrich(new FocusInfo(window.AppId, window.Title, window.Pid)).ProcessName;
        return ContextRuleMatcher.ProcessNameMatches(ContextRuleMatcher.Normalize(name),
            ContextRuleMatcher.Normalize(processName));
    }

    private static Backend SelectBackend()
    {
        bool wayland = LinuxSystemFacts.SessionType() == "wayland";

        if (wayland && CompositorDetector.CreateActivator() is { } compositor)
            return new Backend(Kind.Complete, compositor);

        if (wayland && KWinWindowActivator.IsKdeWayland())
            return new Backend(Kind.KWin, null);

        if (wayland && GnomeActivationExtension.IsGnome())
            return new Backend(Kind.Gnome, HasDisplay() ? new X11WindowActivator() : null);

        if (HasDisplay())
            return new Backend(wayland ? Kind.XWaylandOnly : Kind.Complete, new X11WindowActivator());

        return new Backend(Kind.None, null);
    }

    private static bool HasDisplay() => !string.IsNullOrWhiteSpace(LinuxSystemFacts.Display());
}
