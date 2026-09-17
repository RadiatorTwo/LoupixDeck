using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;

namespace LoupixDeck.Services.Diagnostics.Linux.Checks;

/// <summary>
/// Reports membership in the "input" group, which is what grants both /dev/uinput and
/// /dev/input/event* on the distributions the installation script targets.
///
/// It separates "the account is a member" from "this session carries the group", because the
/// gap between the two is the single most common Linux support case: usermod ran, but nobody
/// signed out afterwards.
/// </summary>
public sealed class InputGroupMembershipCheck : ILinuxDiagnosticCheck
{
    /// <summary>The group the installation script adds the user to.</summary>
    public const string InputGroupName = "input";

    public string Id => "uinput.group";

    public DiagnosticCategory Category => DiagnosticCategory.InputInjection;

    public Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string title = DiagnosticCheckTitles.For(Id);
        GroupMembership membership = LinuxGroupFacts.Resolve(InputGroupName);

        if (!membership.Exists)
        {
            return Task.FromResult(DiagnosticCheckResult.Warning(Id, Category, title,
                Loc.Tr("Diagnostics_GroupNoSuchGroup")));
        }

        Dictionary<string, string> evidence = new(StringComparer.Ordinal)
        {
            ["gid"] = membership.GroupId?.ToString() ?? "(unknown)",
            ["effective"] = membership.Effective ? "yes" : "no",
            ["configured"] = membership.Configured ? "yes" : "no"
        };

        if (membership.Effective)
        {
            return Task.FromResult(DiagnosticCheckResult.Pass(Id, Category, title,
                Loc.Tr("Diagnostics_GroupEffective"), null, evidence));
        }

        if (membership.Configured)
        {
            DiagnosticFix relogin = new(FixKind.Manual, Loc.Tr("Diagnostics_FixRelogin"),
                RequiresLogout: true);

            return Task.FromResult(DiagnosticCheckResult.Warning(Id, Category, title,
                Loc.Tr("Diagnostics_GroupPendingLogout"), null, relogin, evidence));
        }

        DiagnosticFix join = new(FixKind.Command, Loc.Tr("Diagnostics_FixJoinInputGroup"),
            "sudo usermod -aG input $USER", RequiresElevation: true, RequiresLogout: true);

        return Task.FromResult(DiagnosticCheckResult.Fail(Id, Category, title,
            Loc.Tr("Diagnostics_GroupMissing"), null, join, evidence));
    }
}
