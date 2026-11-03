using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;
using LoupixDeck.Services.ActiveWindow;

namespace LoupixDeck.Services.Diagnostics.Linux.Checks;

/// <summary>
/// Shows what the app currently believes the foreground window is - the value every
/// app-switching rule is matched against. An empty snapshot reads differently per
/// running monitor: with the native Wayland monitor or a live xprop spy it means
/// nothing has changed since start, while the xprop monitor on a Wayland session
/// without a display is the real "page switching cannot work here" state - the
/// monitor decides which message is true.
/// </summary>
public sealed class ActiveWindowCheck(IActiveWindowState state, IActiveWindowMonitor monitor)
    : ILinuxDiagnosticCheck
{
    public string Id => "session.active-window";

    public DiagnosticCategory Category => DiagnosticCategory.Session;

    public Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string title = DiagnosticCheckTitles.For(Id);
        ActiveWindowInfo current = state.Current;

        if (string.IsNullOrWhiteSpace(current?.ProcessName))
        {
            // The xprop monitor on Wayland without an X server can never see a window:
            // the feature is genuinely unavailable there. Everywhere else an empty
            // snapshot is only "quiet since start", so say that instead of claiming a
            // limitation.
            bool unavailable = monitor.Kind == ActiveWindowMonitorKind.XProp
                && LinuxSystemFacts.SessionType() == "wayland"
                && string.IsNullOrWhiteSpace(LinuxSystemFacts.Display());

            if (unavailable)
            {
                DiagnosticFix fix = new(FixKind.Manual, Loc.Tr("Diagnostics_FixUseX11Session"),
                    RequiresLogout: true);

                return Task.FromResult(DiagnosticCheckResult.Warning(Id, Category, title,
                    Loc.Tr("Diagnostics_ActiveWindowPureWayland"), null, fix, null,
                    Loc.Tr("Diagnostics_ValueUnsupported")));
            }

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