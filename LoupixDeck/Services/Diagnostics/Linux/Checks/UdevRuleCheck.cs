using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;

namespace LoupixDeck.Services.Diagnostics.Linux.Checks;

/// <summary>
/// Looks for the udev rule the installation script writes, and specifically for its uinput line.
///
/// A missing rule only warns: many distributions already ship /dev/uinput as 0660 root:input, so
/// the rule is not the ground truth - the write-access check is. Comparing the installed rule
/// against the full shipped template is phase 2.
/// </summary>
public sealed class UdevRuleCheck : ILinuxDiagnosticCheck
{
    private static readonly string[] RulePaths =
    [
        "/etc/udev/rules.d/99-loupixdeck.rules",
        "/usr/lib/udev/rules.d/99-loupixdeck.rules"
    ];

    public string Id => "uinput.udev-rule";

    public DiagnosticCategory Category => DiagnosticCategory.InputInjection;

    public Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string title = DiagnosticCheckTitles.For(Id);
        DiagnosticFix installer = new(FixKind.InstallerScript, Loc.Tr("Diagnostics_FixRunInstaller"),
            DiagnosticInstaller.Command(), RequiresElevation: true);

        foreach (string path in RulePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string content;

            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                content = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                return Task.FromResult(DiagnosticCheckResult.Unknown(Id, Category, title,
                    Loc.Tr("Diagnostics_UdevRuleUnreadable"), $"{path}: {ex.Message}"));
            }

            Dictionary<string, string> evidence = new(StringComparer.Ordinal)
            {
                ["rule_file"] = path
            };

            bool hasUinputRule = content.Contains("KERNEL==\"uinput\"", StringComparison.Ordinal) &&
                                 content.Contains("GROUP=\"input\"", StringComparison.Ordinal);

            if (hasUinputRule)
            {
                return Task.FromResult(DiagnosticCheckResult.Pass(Id, Category, title,
                    Loc.Tr("Diagnostics_UdevRulePresent"), null, evidence));
            }

            return Task.FromResult(DiagnosticCheckResult.Warning(Id, Category, title,
                Loc.Tr("Diagnostics_UdevRuleWithoutUinput"), null, installer, evidence));
        }

        return Task.FromResult(DiagnosticCheckResult.Warning(Id, Category, title,
            Loc.Tr("Diagnostics_UdevRuleMissing"), null, installer));
    }
}
