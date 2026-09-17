using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using LoupixDeck.Localization;
using LoupixDeck.Models.Diagnostics;
using LoupixDeck.Services.Diagnostics.Linux;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels.Diagnostics;

/// <summary>One entry of the category column: a dot, the name, and how it went.</summary>
public sealed partial class DiagnosticCategoryViewModel : ViewModelBase
{
    public DiagnosticCategoryViewModel(DiagnosticCategory category)
    {
        Category = category;
        Checks = [];
    }

    public DiagnosticCategory Category { get; }

    public string Title => DiagnosticText.CategoryTitle(Category);

    /// <summary>The checks of this category, worst first.</summary>
    public ObservableCollection<DiagnosticCheckRowViewModel> Checks { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusBrush))]
    public partial DiagnosticStatus Status { get; set; } = DiagnosticStatus.Pass;

    /// <summary>The short right-hand note: what is wrong, or how many passed.</summary>
    [ObservableProperty]
    public partial string MetaText { get; set; } = string.Empty;

    public IBrush StatusBrush => DiagnosticPalette.Dot(Status);

    /// <summary>Re-sorts the checks and recomputes the dot and the note.</summary>
    public void Refresh()
    {
        List<DiagnosticCheckRowViewModel> ordered = Checks
            .OrderBy(row => row.Rank)
            .ThenBy(row => row.Title, StringComparer.CurrentCulture)
            .ToList();

        for (int index = 0; index < ordered.Count; index++)
        {
            int current = Checks.IndexOf(ordered[index]);

            if (current != index)
            {
                Checks.Move(current, index);
            }
        }

        Status = DiagnosticText.Worst(Checks.Select(row => row.Result.Status));

        int failed = Checks.Count(row => row.Result.Status == DiagnosticStatus.Fail);
        int warnings = Checks.Count(row => row.Result.Status == DiagnosticStatus.Warning);
        int unknown = Checks.Count(row => row.Result.Status == DiagnosticStatus.Unknown);

        MetaText = failed > 0
            ? Loc.Tr("Diagnostics_MetaFailedFmt", failed)
            : warnings > 0
                ? Loc.Tr("Diagnostics_MetaWarningsFmt", warnings)
                : unknown > 0
                    ? Loc.Tr("Diagnostics_MetaUnknownFmt", unknown)
                    : Loc.Tr("Diagnostics_MetaOkFmt", Checks.Count);
    }

    /// <summary>The check this category should open on: the worst one.</summary>
    public DiagnosticCheckRowViewModel FirstWorst() => Checks.FirstOrDefault();
}
