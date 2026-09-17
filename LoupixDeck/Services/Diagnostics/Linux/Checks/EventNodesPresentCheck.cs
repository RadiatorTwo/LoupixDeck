using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;

namespace LoupixDeck.Services.Diagnostics.Linux.Checks;

/// <summary>
/// Checks that /dev/input carries event devices at all. Only the count is reported: device names
/// identify the user's hardware and have no place in a copied report.
/// </summary>
public sealed class EventNodesPresentCheck : ILinuxDiagnosticCheck
{
    public string Id => "recording.event-nodes";

    public DiagnosticCategory Category => DiagnosticCategory.InputRecording;

    public Task<DiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string title = DiagnosticCheckTitles.For(Id);
        int count;

        try
        {
            count = Directory.GetFiles("/dev/input", "event*").Length;
        }
        catch (Exception ex)
        {
            return Task.FromResult(DiagnosticCheckResult.Unknown(Id, Category, title,
                Loc.Tr("Diagnostics_EventNodesUnreadable"), ex.Message));
        }

        Dictionary<string, string> evidence = new(StringComparer.Ordinal)
        {
            ["count"] = count.ToString()
        };

        if (count == 0)
        {
            return Task.FromResult(DiagnosticCheckResult.Fail(Id, Category, title,
                Loc.Tr("Diagnostics_EventNodesNone"), null, null, evidence));
        }

        return Task.FromResult(DiagnosticCheckResult.Pass(Id, Category, title,
            Loc.Tr("Diagnostics_EventNodesFoundFmt", count), null, evidence));
    }
}
