using System.Runtime.InteropServices;
using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;

namespace LoupixDeck.Services.Diagnostics.Linux.Checks;

/// <summary>
/// Reports the architecture. A process architecture that differs from the OS architecture means
/// the app runs under emulation, which is worth saying out loud in a bug report.
/// </summary>
public sealed class ArchitectureCheck : ILinuxDiagnosticCheck
{
    public string Id => "system.architecture";

    public DiagnosticCategory Category => DiagnosticCategory.System;

    public Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string title = DiagnosticCheckTitles.For(Id);
        (Architecture os, Architecture process) = LinuxSystemFacts.Architectures();

        Dictionary<string, string> evidence = new(StringComparer.Ordinal)
        {
            ["os"] = os.ToString(),
            ["process"] = process.ToString()
        };

        if (os != process)
        {
            return Task.FromResult(DiagnosticCheckResult.Warning(Id, Category, title,
                Loc.Tr("Diagnostics_ArchitectureMismatchFmt", process, os), null, null, evidence,
                $"{process} / {os}"));
        }

        return Task.FromResult(DiagnosticCheckResult.Pass(Id, Category, title,
            os.ToString(), null, evidence, os.ToString()));
    }
}
