using Avalonia.Media;
using LoupixDeck.Models.Diagnostics;
using LoupixDeck.Services.Diagnostics.Linux;

namespace LoupixDeck.ViewModels.Diagnostics;

/// <summary>
/// One counter pill in the header. A count of zero is drawn flat and grey, so the pills that
/// matter are the ones that carry colour.
/// </summary>
public sealed class DiagnosticTallyViewModel
{
    public DiagnosticTallyViewModel(DiagnosticStatus status, int count, string label)
    {
        bool empty = count == 0;

        Label = label;
        Text = empty ? DiagnosticPalette.NeutralText : DiagnosticPalette.Text(status);
        Fill = empty ? DiagnosticPalette.EmptyFill : DiagnosticPalette.Fill(status);
        Border = empty ? DiagnosticPalette.EmptyBorder : DiagnosticPalette.Border(status);
    }

    public string Label { get; }

    public IBrush Text { get; }

    public IBrush Fill { get; }

    public IBrush Border { get; }

    /// <summary>The five pills for one run, in the order the header shows them.</summary>
    public static IReadOnlyList<DiagnosticTallyViewModel> For(IEnumerable<DiagnosticCheckResult> results)
    {
        List<DiagnosticCheckResult> all = results.ToList();

        return
        [
            Build(all, DiagnosticStatus.Pass, "Diagnostics_TallyPassedFmt"),
            Build(all, DiagnosticStatus.Warning, "Diagnostics_TallyWarningsFmt"),
            Build(all, DiagnosticStatus.Fail, "Diagnostics_TallyFailedFmt"),
            Build(all, DiagnosticStatus.Unknown, "Diagnostics_TallyUnknownFmt"),
            Build(all, DiagnosticStatus.Skipped, "Diagnostics_TallySkippedFmt")
        ];
    }

    private static DiagnosticTallyViewModel Build(IReadOnlyList<DiagnosticCheckResult> all,
        DiagnosticStatus status, string labelKey)
    {
        int count = all.Count(result => result.Status == status);

        return new DiagnosticTallyViewModel(status, count, Localization.Loc.Tr(labelKey, count));
    }
}
