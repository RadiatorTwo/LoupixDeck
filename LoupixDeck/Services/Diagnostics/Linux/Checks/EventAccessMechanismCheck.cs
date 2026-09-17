using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;

namespace LoupixDeck.Services.Diagnostics.Linux.Checks;

/// <summary>
/// Explains how read access to /dev/input is granted, which decides whether it survives.
/// Group membership holds everywhere; a logind ACL exists only in an active local session and
/// is gone over SSH or after a fast user switch.
///
/// The mechanism is inferred from the probe result and the group membership. getfacl is not used
/// on purpose: it ships in a separate package on several distributions, and a missing binary
/// would turn a perfectly working system into an unknown result.
/// </summary>
public sealed class EventAccessMechanismCheck : ILinuxDiagnosticCheck, IExclusiveDiagnosticCheck
{
    public string Id => "recording.access-mechanism";

    public DiagnosticCategory Category => DiagnosticCategory.InputRecording;

    public Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string title = DiagnosticCheckTitles.For(Id);
        IReadOnlyList<EventDeviceCandidate> candidates = LinuxEventDeviceFacts.Discover();

        if (candidates == null)
        {
            return Task.FromResult(DiagnosticCheckResult.Unknown(Id, Category, title,
                Loc.Tr("Diagnostics_EventListUnreadable")));
        }

        (int keyboards, int _, int _) = LinuxEventDeviceFacts.ProbeReadable(candidates, cancellationToken);

        if (keyboards == 0)
        {
            return Task.FromResult(DiagnosticCheckResult.Skipped(Id, Category, title,
                Loc.Tr("Diagnostics_AccessNothingToExplain")));
        }

        GroupMembership membership = LinuxGroupFacts.Resolve(InputGroupMembershipCheck.InputGroupName);

        if (membership.Effective)
        {
            return Task.FromResult(DiagnosticCheckResult.Pass(Id, Category, title,
                Loc.Tr("Diagnostics_AccessViaGroup")));
        }

        return Task.FromResult(DiagnosticCheckResult.Pass(Id, Category, title,
            Loc.Tr("Diagnostics_AccessViaAcl"), Loc.Tr("Diagnostics_AccessViaAclDetail")));
    }
}
