using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;

namespace LoupixDeck.Services.Diagnostics.Linux.Checks.Installation;

/// <summary>
/// The desktop entry the installer writes - the reason LoupixDeck appears in the application
/// menu at all. It is checked for existence and for pointing at an executable that is really
/// there, because a stale Exec line is what an old installation leaves behind.
/// </summary>
public sealed class DesktopEntryCheck : ILinuxDiagnosticCheck
{
    public string Id => "install.desktop-entry";

    public DiagnosticCategory Category => DiagnosticCategory.Installation;

    public Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string title = DiagnosticCheckTitles.For(Id);

        if (LinuxSystemFacts.DetectInstallMode() == InstallMode.Source)
        {
            // A source build is run from its output folder and writes no desktop entry.
            return Task.FromResult(DiagnosticCheckResult.Skipped(Id, Category, title,
                Loc.Tr("Diagnostics_DesktopEntrySourceBuild"), null, Loc.Tr("Diagnostics_ValueSourceBuild")));
        }

        string path = InstallationPaths.DesktopEntries().FirstOrDefault(File.Exists);

        if (path == null)
        {
            return Task.FromResult(DiagnosticCheckResult.Warning(Id, Category, title,
                Loc.Tr("Diagnostics_DesktopEntryMissing"), null,
                new DiagnosticFix(FixKind.InstallerScript, Loc.Tr("Diagnostics_FixRunInstaller"),
                    DiagnosticInstaller.Command(), RequiresElevation: true), null,
                Loc.Tr("Diagnostics_ValueMissing")));
        }

        string exec = ExecLine(path);

        Dictionary<string, string> evidence = new(StringComparer.Ordinal)
        {
            ["desktop_entry"] = path,
            ["exec"] = exec ?? "?"
        };

        if (string.IsNullOrWhiteSpace(exec))
        {
            return Task.FromResult(DiagnosticCheckResult.Warning(Id, Category, title,
                Loc.Tr("Diagnostics_DesktopEntryNoExec"), path, null, evidence,
                Loc.Tr("Diagnostics_ValueIncomplete")));
        }

        string binary = exec.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim('"');

        if (!File.Exists(binary))
        {
            return Task.FromResult(DiagnosticCheckResult.Fail(Id, Category, title,
                Loc.Tr("Diagnostics_DesktopEntryStaleFmt", binary), path,
                new DiagnosticFix(FixKind.InstallerScript, Loc.Tr("Diagnostics_FixRunInstaller"),
                    DiagnosticInstaller.Command(), RequiresElevation: true), evidence,
                Loc.Tr("Diagnostics_ValueStale")));
        }

        return Task.FromResult(DiagnosticCheckResult.Pass(Id, Category, title,
            Loc.Tr("Diagnostics_DesktopEntryOk"), null, evidence, Loc.Tr("Diagnostics_ValuePresent")));
    }

    /// <summary>The entry's Exec value without the desktop-entry field codes (%u, %F, …).</summary>
    private static string ExecLine(string path)
    {
        try
        {
            string line = File.ReadLines(path)
                .FirstOrDefault(entry => entry.StartsWith("Exec=", StringComparison.Ordinal));

            if (line == null)
            {
                return null;
            }

            string exec = line["Exec=".Length..];
            int field = exec.IndexOf('%');

            return (field >= 0 ? exec[..field] : exec).Trim();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
