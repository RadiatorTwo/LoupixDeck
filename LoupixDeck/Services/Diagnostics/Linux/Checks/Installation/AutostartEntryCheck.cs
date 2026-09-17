using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;

namespace LoupixDeck.Services.Diagnostics.Linux.Checks.Installation;

/// <summary>
/// The XDG autostart entry. It is optional - not having one is a setting, not a defect - so
/// this only reports what is there, and fails only when an entry exists and points at a binary
/// that does not, which is the case where the session silently starts nothing.
/// </summary>
public sealed class AutostartEntryCheck : ILinuxDiagnosticCheck
{
    public string Id => "install.autostart";

    public DiagnosticCategory Category => DiagnosticCategory.Installation;

    public Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string title = DiagnosticCheckTitles.For(Id);
        string path = InstallationPaths.AutostartEntry();

        if ((path == null) || !File.Exists(path))
        {
            return Task.FromResult(DiagnosticCheckResult.Skipped(Id, Category, title,
                Loc.Tr("Diagnostics_AutostartNotConfigured"), null, Loc.Tr("Diagnostics_ValueOff")));
        }

        Dictionary<string, string> evidence = new(StringComparer.Ordinal)
        {
            ["autostart_entry"] = path
        };

        string exec = ExecTarget(path);

        if (string.IsNullOrWhiteSpace(exec))
        {
            return Task.FromResult(DiagnosticCheckResult.Warning(Id, Category, title,
                Loc.Tr("Diagnostics_AutostartNoExec"), path, null, evidence,
                Loc.Tr("Diagnostics_ValueIncomplete")));
        }

        evidence["exec"] = exec;

        if (!File.Exists(exec))
        {
            return Task.FromResult(DiagnosticCheckResult.Fail(Id, Category, title,
                Loc.Tr("Diagnostics_AutostartStaleFmt", exec), path,
                new DiagnosticFix(FixKind.InstallerScript, Loc.Tr("Diagnostics_FixRunInstaller"),
                    DiagnosticInstaller.Command(), RequiresElevation: true), evidence,
                Loc.Tr("Diagnostics_ValueStale")));
        }

        return Task.FromResult(DiagnosticCheckResult.Pass(Id, Category, title,
            Loc.Tr("Diagnostics_AutostartOk"), null, evidence, Loc.Tr("Diagnostics_ValueOn")));
    }

    private static string ExecTarget(string path)
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
            exec = (field >= 0 ? exec[..field] : exec).Trim();

            return exec.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim('"');
        }
        catch (Exception)
        {
            return null;
        }
    }
}
