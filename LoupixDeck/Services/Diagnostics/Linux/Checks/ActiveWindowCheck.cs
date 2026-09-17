using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;
using LoupixDeck.Services.ActiveWindow;

namespace LoupixDeck.Services.Diagnostics.Linux.Checks;

/// <summary>
/// Shows what the app currently believes the foreground window is - the value every
/// app-switching rule is matched against. An empty snapshot is the visible form of "page
/// switching by application does nothing here", which on pure Wayland is a limitation rather
/// than a defect.
/// </summary>
public sealed class ActiveWindowCheck(IActiveWindowState state) : ILinuxDiagnosticCheck
{
    public string Id => "session.active-window";

    public DiagnosticCategory Category => DiagnosticCategory.Session;

    public Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string title = DiagnosticCheckTitles.For(Id);
        ActiveWindowInfo current = state.Current;

        if (string.IsNullOrWhiteSpace(current?.ProcessName))
        {
            bool wayland = string.IsNullOrWhiteSpace(LinuxSystemFacts.Display());

            if (wayland)
            {
                DiagnosticFix fix = new(FixKind.Manual, Loc.Tr("Diagnostics_FixUseX11Session"),
                    RequiresLogout: true);

                return Task.FromResult(DiagnosticCheckResult.Warning(Id, Category, title,
                    Loc.Tr("Diagnostics_ActiveWindowPureWayland"), null, fix, null,
                    Loc.Tr("Diagnostics_ValueUnsupported")));
            }

            // X11 is there but nothing has been reported yet: the monitor has not seen a window
            // change since the app started.
            return Task.FromResult(DiagnosticCheckResult.Warning(Id, Category, title,
                Loc.Tr("Diagnostics_ActiveWindowNone"), null, null, null,
                Loc.Tr("Diagnostics_ValueNone")));
        }

        // Only the process name becomes evidence. The window title is shown as technical
        // detail, where the report's scrubbing still runs over it - a title can carry a file
        // name or an address, and evidence is meant to be the safe part of a report.
        Dictionary<string, string> evidence = new(StringComparer.Ordinal)
        {
            ["process_name"] = current.ProcessName
        };

        return Task.FromResult(DiagnosticCheckResult.Pass(Id, Category, title,
            Loc.Tr("Diagnostics_ActiveWindowOkFmt", current.ProcessName), current.Title, evidence,
            current.ProcessName));
    }
}
