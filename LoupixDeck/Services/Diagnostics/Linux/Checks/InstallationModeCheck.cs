using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;

namespace LoupixDeck.Services.Diagnostics.Linux.Checks;

/// <summary>
/// Reports where this binary runs from. It matters because the installed udev rules, the
/// desktop entry and the group membership belong to an installation - a binary started from a
/// build output can be perfectly healthy while the installed files describe a different one.
/// </summary>
public sealed class InstallationModeCheck : ILinuxDiagnosticCheck
{
    public string Id => "system.install-mode";

    public DiagnosticCategory Category => DiagnosticCategory.System;

    public Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string title = DiagnosticCheckTitles.For(Id);
        InstallMode mode = LinuxSystemFacts.DetectInstallMode();

        Dictionary<string, string> evidence = new(StringComparer.Ordinal)
        {
            ["base_directory"] = AppContext.BaseDirectory
        };

        return Task.FromResult(mode switch
        {
            InstallMode.System => DiagnosticCheckResult.Pass(Id, Category, title,
                Loc.Tr("Diagnostics_InstallModeSystem"), null, evidence),
            InstallMode.User => DiagnosticCheckResult.Pass(Id, Category, title,
                Loc.Tr("Diagnostics_InstallModeUser"), null, evidence),
            InstallMode.SteamOs => DiagnosticCheckResult.Pass(Id, Category, title,
                Loc.Tr("Diagnostics_InstallModeSteamOs"), null, evidence),
            InstallMode.Source => DiagnosticCheckResult.Warning(Id, Category, title,
                Loc.Tr("Diagnostics_InstallModeSource"), null, null, evidence),
            _ => DiagnosticCheckResult.Unknown(Id, Category, title,
                Loc.Tr("Diagnostics_InstallModeUnknown"))
        });
    }
}
