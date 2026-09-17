using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;

namespace LoupixDeck.Services.Diagnostics.Linux.Checks;

/// <summary>
/// On a Wayland session, reports whether an X server is reachable for LinuxActiveWindowMonitor.
/// Pure Wayland is reported as a warning rather than a failure: it is a supported but limited
/// configuration, and a user who never switches pages by application loses nothing.
/// </summary>
public sealed class XWaylandCheck : ILinuxDiagnosticCheck
{
    public string Id => "session.xwayland";

    public DiagnosticCategory Category => DiagnosticCategory.Session;

    public Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string title = DiagnosticCheckTitles.For(Id);
        string sessionType = LinuxSystemFacts.SessionType();
        string display = LinuxSystemFacts.Display();

        if (string.IsNullOrWhiteSpace(sessionType))
        {
            return Task.FromResult(DiagnosticCheckResult.Unknown(Id, Category, title,
                Loc.Tr("Diagnostics_SessionTypeUnknown")));
        }

        if (sessionType == "x11")
        {
            return Task.FromResult(DiagnosticCheckResult.Skipped(Id, Category, title,
                Loc.Tr("Diagnostics_XWaylandNotApplicable")));
        }

        if (string.IsNullOrWhiteSpace(display))
        {
            DiagnosticFix fix = new(FixKind.Manual, Loc.Tr("Diagnostics_FixUseX11Session"),
                RequiresLogout: true);

            return Task.FromResult(DiagnosticCheckResult.Warning(Id, Category, title,
                Loc.Tr("Diagnostics_SessionPureWayland"), null, fix));
        }

        Dictionary<string, string> evidence = new(StringComparer.Ordinal)
        {
            ["display"] = display
        };

        string socket = SocketPathFor(display);

        if ((socket != null) && !File.Exists(socket) && !Directory.Exists(socket))
        {
            return Task.FromResult(DiagnosticCheckResult.Warning(Id, Category, title,
                Loc.Tr("Diagnostics_XWaylandSocketMissingFmt", display), socket, null, evidence));
        }

        return Task.FromResult(DiagnosticCheckResult.Pass(Id, Category, title,
            Loc.Tr("Diagnostics_XWaylandAvailable"), null, evidence));
    }

    /// <summary>
    /// The unix socket a local display number maps to, or null for a remote display such as
    /// "localhost:10.0", where there is no local socket to look for.
    /// </summary>
    private static string SocketPathFor(string display)
    {
        int colon = display.IndexOf(':');

        if (colon != 0)
        {
            return null;
        }

        string number = display[(colon + 1)..];
        int dot = number.IndexOf('.');

        if (dot >= 0)
        {
            number = number[..dot];
        }

        return int.TryParse(number, out int _) ? $"/tmp/.X11-unix/X{number}" : null;
    }
}
