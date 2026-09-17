using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;

namespace LoupixDeck.Services.Diagnostics.Linux.Checks.Installation;

/// <summary>
/// The "loupixdeck" launcher on PATH and the install directories behind it. Two install
/// directories at once is the remnant case: a system-wide installation that was later replaced
/// by a user one leaves the old copy, and which of the two the menu entry starts is then a
/// coin toss.
/// </summary>
public sealed class LauncherCheck : ILinuxDiagnosticCheck
{
    public string Id => "install.launcher";

    public DiagnosticCategory Category => DiagnosticCategory.Installation;

    public Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string title = DiagnosticCheckTitles.For(Id);

        if (LinuxSystemFacts.DetectInstallMode() == InstallMode.Source)
        {
            return Task.FromResult(DiagnosticCheckResult.Skipped(Id, Category, title,
                Loc.Tr("Diagnostics_LauncherSourceBuild"), null, Loc.Tr("Diagnostics_ValueSourceBuild")));
        }

        List<string> launchers = InstallationPaths.Launchers().Where(File.Exists).ToList();
        List<string> installs = InstallationPaths.InstallDirectories().Where(Directory.Exists).ToList();

        Dictionary<string, string> evidence = new(StringComparer.Ordinal)
        {
            ["launchers"] = launchers.Count == 0 ? "none" : string.Join(", ", launchers),
            ["install_dirs"] = installs.Count == 0 ? "none" : string.Join(", ", installs),
            ["running_from"] = AppContext.BaseDirectory
        };

        if (installs.Count > 1)
        {
            return Task.FromResult(DiagnosticCheckResult.Warning(Id, Category, title,
                Loc.Tr("Diagnostics_LauncherDuplicateInstall"), string.Join("\n", installs),
                new DiagnosticFix(FixKind.Manual, Loc.Tr("Diagnostics_FixRemoveOldInstall")), evidence,
                Loc.Tr("Diagnostics_ValueDuplicate")));
        }

        if (launchers.Count == 0)
        {
            return Task.FromResult(DiagnosticCheckResult.Warning(Id, Category, title,
                Loc.Tr("Diagnostics_LauncherMissing"), null,
                new DiagnosticFix(FixKind.InstallerScript, Loc.Tr("Diagnostics_FixRunInstaller"),
                    DiagnosticInstaller.Command(), RequiresElevation: true), evidence,
                Loc.Tr("Diagnostics_ValueMissing")));
        }

        return Task.FromResult(DiagnosticCheckResult.Pass(Id, Category, title,
            Loc.Tr("Diagnostics_LauncherOkFmt", launchers[0]), null, evidence,
            Loc.Tr("Diagnostics_ValuePresent")));
    }
}
