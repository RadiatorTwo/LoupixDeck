using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;
using LoupixDeck.Services.ActiveWindow;
using LoupixDeck.Services.AppActivation;
using LoupixDeck.WaylandFocus;

namespace LoupixDeck.Services.Diagnostics.Linux.Checks;

/// <summary>
/// Reports which focus compositor the session exposes - or that none of the supported
/// ones (COSMIC, sway, niri, Hyprland) answers, which is why page switching by
/// application falls back to xprop/XWayland. The detection result plus the running
/// monitor kind in the evidence hand a bug report the two facts that decide the
/// feature's behaviour, without asking the user for a desktop dump.
/// </summary>
public sealed class CompositorCheck(IActiveWindowMonitor monitor) : ILinuxDiagnosticCheck
{
    public string Id => "session.compositor";

    public DiagnosticCategory Category => DiagnosticCategory.Session;

    public Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string title = DiagnosticCheckTitles.For(Id);
        string sessionType = LinuxSystemFacts.SessionType();

        if (string.IsNullOrWhiteSpace(sessionType))
        {
            return Task.FromResult(DiagnosticCheckResult.Unknown(Id, Category, title,
                Loc.Tr("Diagnostics_SessionTypeUnknown")));
        }

        if (sessionType != "wayland")
        {
            return Task.FromResult(DiagnosticCheckResult.Skipped(Id, Category, title,
                Loc.Tr("Diagnostics_CompositorNotApplicable"), null, sessionType));
        }

        // GNOME has no focus IPC of its own; what it needs is covered by the separate
        // session.gnome-activation check, so a warning here would only repeat it.
        if (GnomeActivationExtension.IsGnome())
        {
            return Task.FromResult(DiagnosticCheckResult.Skipped(Id, Category, title,
                Loc.Tr("Diagnostics_CompositorGnome"), null, "GNOME"));
        }

        // The environment is fixed for the session, so live detection returns the same
        // answer the monitor registration made; the monitor kind sits beside it in the
        // evidence, so a disagreement between the two would show up in a report.
        bool detected = CompositorDetector.TryDetect(out string compositor);

        Dictionary<string, string> evidence = new(StringComparer.Ordinal)
        {
            ["compositor"] = detected ? compositor : "none",
            ["active_window_monitor"] = monitor.Kind.ToString().ToLowerInvariant()
        };

        if (detected)
        {
            return Task.FromResult(DiagnosticCheckResult.Pass(Id, Category, title,
                Loc.Tr("Diagnostics_CompositorDetectedFmt", compositor), null, evidence,
                compositor));
        }

        DiagnosticFix fix = new(FixKind.Manual, Loc.Tr("Diagnostics_FixUseX11Session"),
            RequiresLogout: true);

        return Task.FromResult(DiagnosticCheckResult.Warning(Id, Category, title,
            Loc.Tr("Diagnostics_CompositorUnsupported"), null, fix, evidence,
            Loc.Tr("Diagnostics_ValueNone")));
    }
}