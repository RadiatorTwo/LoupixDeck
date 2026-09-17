using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;

namespace LoupixDeck.Services.Diagnostics.Linux.Checks;

/// <summary>
/// Reports whether this is an X11, a Wayland or a text session. The session type decides whether
/// switching pages by the active application can work at all - LinuxActiveWindowMonitor drives
/// that through xprop and needs an X server, native or XWayland.
/// </summary>
public sealed class SessionTypeCheck : ILinuxDiagnosticCheck
{
    public string Id => "session.type";

    public DiagnosticCategory Category => DiagnosticCategory.Session;

    public Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string title = DiagnosticCheckTitles.For(Id);
        string sessionType = LinuxSystemFacts.SessionType();
        bool hasDisplay = !string.IsNullOrWhiteSpace(LinuxSystemFacts.Display());

        Dictionary<string, string> evidence = new(StringComparer.Ordinal)
        {
            ["session_type"] = sessionType ?? "(unset)",
            ["display"] = hasDisplay ? "set" : "(unset)",
            ["wayland_display"] = string.IsNullOrWhiteSpace(LinuxSystemFacts.WaylandDisplay())
                ? "(unset)"
                : "set"
        };

        if (string.IsNullOrWhiteSpace(sessionType))
        {
            return Task.FromResult(DiagnosticCheckResult.Unknown(Id, Category, title,
                Loc.Tr("Diagnostics_SessionTypeUnknown")));
        }

        return Task.FromResult(sessionType switch
        {
            "x11" => DiagnosticCheckResult.Pass(Id, Category, title,
                Loc.Tr("Diagnostics_SessionX11"), null, evidence),
            "wayland" when hasDisplay => DiagnosticCheckResult.Pass(Id, Category, title,
                Loc.Tr("Diagnostics_SessionXWayland"), null, evidence),
            "wayland" => DiagnosticCheckResult.Warning(Id, Category, title,
                Loc.Tr("Diagnostics_SessionPureWayland"), null, null, evidence),
            "tty" => DiagnosticCheckResult.Warning(Id, Category, title,
                Loc.Tr("Diagnostics_SessionTty"), null, null, evidence),
            _ => DiagnosticCheckResult.Unknown(Id, Category, title,
                Loc.Tr("Diagnostics_SessionTypeUnknown"))
        });
    }
}
