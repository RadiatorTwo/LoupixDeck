using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;
using LoupixDeck.Services.Diagnostics.Linux;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels.Diagnostics;

/// <summary>
/// One expandable group of checks. It starts expanded only when it has something to say, so a
/// healthy system shows four collapsed rows rather than a wall of green.
/// </summary>
public sealed partial class DiagnosticCategoryViewModel : ViewModelBase
{
    public DiagnosticCategoryViewModel(DiagnosticCategory category)
    {
        Category = category;
        Checks = [];
    }

    public DiagnosticCategory Category { get; }

    public string Title => DiagnosticText.CategoryTitle(Category);

    /// <summary>The recording category carries the playback-is-not-recording hint.</summary>
    public bool ShowRecordingHint => Category == DiagnosticCategory.InputRecording;

    public ObservableCollection<DiagnosticCheckRowViewModel> Checks { get; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    public partial string CountsText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasProblems { get; set; }

    /// <summary>Recomputes the header line from the rows currently in the group.</summary>
    public void RefreshCounts()
    {
        int passed = Checks.Count(row => row.IsPass);
        int warnings = Checks.Count(row => row.IsWarning);
        int failed = Checks.Count(row => row.IsFail);
        int unknown = Checks.Count(row => row.Result.Status == DiagnosticStatus.Unknown);
        int skipped = Checks.Count(row => row.Result.Status == DiagnosticStatus.Skipped);

        CountsText = Loc.Tr("Diagnostics_CountsFmt", passed, warnings, failed, unknown, skipped);
        HasProblems = (warnings + failed + unknown) > 0;
        IsExpanded = HasProblems;
    }
}
