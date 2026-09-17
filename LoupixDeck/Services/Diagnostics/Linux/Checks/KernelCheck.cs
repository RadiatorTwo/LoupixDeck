using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;

namespace LoupixDeck.Services.Diagnostics.Linux.Checks;

/// <summary>Reports the running kernel release.</summary>
public sealed class KernelCheck : ILinuxDiagnosticCheck
{
    public string Id => "system.kernel";

    public DiagnosticCategory Category => DiagnosticCategory.System;

    public Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string title = DiagnosticCheckTitles.For(Id);
        string release = LinuxSystemFacts.KernelRelease();

        if (string.IsNullOrWhiteSpace(release))
        {
            return Task.FromResult(DiagnosticCheckResult.Unknown(Id, Category, title,
                Loc.Tr("Diagnostics_KernelUnreadable")));
        }

        return Task.FromResult(DiagnosticCheckResult.Pass(Id, Category, title, release));
    }
}
