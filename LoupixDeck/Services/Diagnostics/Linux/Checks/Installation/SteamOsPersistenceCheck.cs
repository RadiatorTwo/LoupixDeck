using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;

namespace LoupixDeck.Services.Diagnostics.Linux.Checks.Installation;

/// <summary>
/// On SteamOS and the other atomic distributions an A/B system update replaces /etc, and the
/// udev rule goes with it - the deck then stops working for no reason the user can see. The
/// installer registers the rule in the keep list to prevent that; this check reports whether
/// that registration is actually there.
/// </summary>
public sealed class SteamOsPersistenceCheck : ILinuxDiagnosticCheck
{
    public string Id => "install.atomic-keep";

    public DiagnosticCategory Category => DiagnosticCategory.Installation;

    public Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string title = DiagnosticCheckTitles.For(Id);
        bool atomic = LinuxSystemFacts.IsSteamOs() ||
                      Directory.Exists(Path.GetDirectoryName(InstallationPaths.AtomicKeepFile));

        if (!atomic)
        {
            return Task.FromResult(DiagnosticCheckResult.Skipped(Id, Category, title,
                Loc.Tr("Diagnostics_AtomicKeepNotApplicable"), null,
                Loc.Tr("Diagnostics_ValueNotApplicable")));
        }

        Dictionary<string, string> evidence = new(StringComparer.Ordinal)
        {
            ["keep_file"] = InstallationPaths.AtomicKeepFile
        };

        if (!File.Exists(InstallationPaths.AtomicKeepFile))
        {
            return Task.FromResult(DiagnosticCheckResult.Warning(Id, Category, title,
                Loc.Tr("Diagnostics_AtomicKeepMissing"), null,
                new DiagnosticFix(FixKind.InstallerScript, Loc.Tr("Diagnostics_FixRunInstaller"),
                    DiagnosticInstaller.Command(), RequiresElevation: true), evidence,
                Loc.Tr("Diagnostics_ValueMissing")));
        }

        return Task.FromResult(DiagnosticCheckResult.Pass(Id, Category, title,
            Loc.Tr("Diagnostics_AtomicKeepOk"), null, evidence, Loc.Tr("Diagnostics_ValuePresent")));
    }
}
