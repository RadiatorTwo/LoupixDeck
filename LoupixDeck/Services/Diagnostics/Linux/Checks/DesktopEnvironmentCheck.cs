using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;

namespace LoupixDeck.Services.Diagnostics.Linux.Checks;

/// <summary>Reports the desktop environment, for context in a bug report.</summary>
public sealed class DesktopEnvironmentCheck : ILinuxDiagnosticCheck
{
    public string Id => "session.desktop";

    public DiagnosticCategory Category => DiagnosticCategory.Session;

    public Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string title = DiagnosticCheckTitles.For(Id);
        string desktop = LinuxSystemFacts.DesktopEnvironment();

        if (string.IsNullOrWhiteSpace(desktop))
        {
            return Task.FromResult(DiagnosticCheckResult.Unknown(Id, Category, title,
                Loc.Tr("Diagnostics_DesktopUnknown")));
        }

        return Task.FromResult(DiagnosticCheckResult.Pass(Id, Category, title, desktop));
    }
}
