using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Localization;
using LoupixDeck.Models;
using LoupixDeck.Models.Diagnostics;
using LoupixDeck.Services;
using LoupixDeck.Services.Diagnostics.Linux;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels.Diagnostics;

/// <summary>
/// Drives the Linux Diagnostics page: category column, check column, detail pane.
///
/// Nothing runs on its own. The page opens empty with a Run button, because one check creates
/// and destroys a virtual input device and Device Doctor must change nothing without an
/// explicit action.
/// </summary>
public sealed partial class LinuxDiagnosticsViewModel : ViewModelBase
{
    private readonly ILinuxDiagnosticsService _diagnostics;
    private readonly IDialogService _dialogService;

    private CancellationTokenSource _run;
    private DiagnosticRunResult _lastRun;

    public LinuxDiagnosticsViewModel(ILinuxDiagnosticsService diagnostics, IDialogService dialogService)
    {
        _diagnostics = diagnostics;
        _dialogService = dialogService;
        Categories = [];
        Tallies = [];
    }

    /// <summary>The category column, in the order the checks are registered.</summary>
    public ObservableCollection<DiagnosticCategoryViewModel> Categories { get; }

    /// <summary>The counter pills in the header.</summary>
    public ObservableCollection<DiagnosticTallyViewModel> Tallies { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleChecks))]
    public partial DiagnosticCategoryViewModel SelectedCategory { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial DiagnosticCheckRowViewModel SelectedCheck { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowReportCommand))]
    [NotifyCanExecuteChangedFor(nameof(RerunCheckCommand))]
    [NotifyPropertyChangedFor(nameof(HasResults))]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShowReportCommand))]
    [NotifyPropertyChangedFor(nameof(HasResults))]
    public partial bool HasRun { get; set; }

    [ObservableProperty]
    public partial int CompletedCount { get; set; }

    [ObservableProperty]
    public partial int TotalCount { get; set; }

    /// <summary>The line under the page title: how many checks, and when they last ran.</summary>
    [ObservableProperty]
    public partial string HeaderText { get; set; } = string.Empty;

    /// <summary>True once a finished run is available to report on.</summary>
    public bool HasResults => HasRun && !IsRunning;

    public bool HasSelection => SelectedCheck != null;

    /// <summary>The checks of the selected category.</summary>
    public IReadOnlyList<DiagnosticCheckRowViewModel> VisibleChecks =>
        SelectedCategory?.Checks ?? (IReadOnlyList<DiagnosticCheckRowViewModel>)[];

    public IAsyncRelayCommand RunCommand => field ??= Relay.Create(RunAllAsync, () => !IsRunning);

    public IRelayCommand CancelCommand => field ??= Relay.Create(Cancel, () => IsRunning);

    public IAsyncRelayCommand ShowReportCommand => field ??= Relay.Create(ShowReportAsync, () => HasResults);

    public IAsyncRelayCommand RerunCheckCommand =>
        field ??= Relay.Create(RerunCheckAsync, () => !IsRunning && (SelectedCheck != null));

    public IRelayCommand<DiagnosticCategoryViewModel> SelectCategoryCommand =>
        field ??= Relay.Create<DiagnosticCategoryViewModel>(SelectCategory);

    public IRelayCommand<DiagnosticCheckRowViewModel> SelectCheckCommand =>
        field ??= Relay.Create<DiagnosticCheckRowViewModel>(SelectCheck);

    private void SelectCategory(DiagnosticCategoryViewModel category)
    {
        if (category == null)
        {
            return;
        }

        foreach (DiagnosticCategoryViewModel entry in Categories)
        {
            entry.IsSelected = entry == category;
        }

        SelectedCategory = category;
        OnPropertyChanged(nameof(VisibleChecks));

        // Open on the worst check of the category, which is the first one after sorting.
        SelectCheck(category.FirstWorst());
    }

    private void SelectCheck(DiagnosticCheckRowViewModel check)
    {
        foreach (DiagnosticCheckRowViewModel row in Categories.SelectMany(category => category.Checks))
        {
            row.IsSelected = row == check;
        }

        SelectedCheck = check;
    }

    private Task RunAllAsync() => ExecuteAsync(() => _diagnostics.RunAllAsync(BuildProgress(), _run.Token),
        _diagnostics.CheckCount);

    private Task RerunCheckAsync()
    {
        string id = SelectedCheck?.Id;

        return string.IsNullOrEmpty(id)
            ? Task.CompletedTask
            : ExecuteAsync(() => _diagnostics.RunCheckAsync(id, BuildProgress(), _run.Token), 1);
    }

    private async Task ExecuteAsync(Func<Task<DiagnosticRunResult>> run, int total)
    {
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        CompletedCount = 0;
        TotalCount = total;
        HeaderText = Loc.Tr("Diagnostics_RunningFmt", 0, total);

        _run = new CancellationTokenSource();

        try
        {
            Merge(await run());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Diagnostics] The run failed: {ex.Message}");
        }
        finally
        {
            _run?.Dispose();
            _run = null;
            IsRunning = false;
            HasRun = true;

            // After IsRunning, so a progress callback that lands late cannot overwrite the
            // finished header with "running check N of M".
            Refresh();
        }
    }

    /// <summary>
    /// Progress<T> captures the UI SynchronizationContext, so its callback already runs on the
    /// UI thread and may touch the collections directly.
    /// </summary>
    private IProgress<DiagnosticCheckResult> BuildProgress() => new Progress<DiagnosticCheckResult>(Apply);

    private void Cancel() => _run?.Cancel();

    /// <summary>Places one finished result in its category, replacing an earlier run's row.</summary>
    private void Apply(DiagnosticCheckResult result)
    {
        DiagnosticCategoryViewModel category =
            Categories.FirstOrDefault(entry => entry.Category == result.Category);

        if (category == null)
        {
            category = new DiagnosticCategoryViewModel(result.Category);
            Categories.Add(category);
        }

        DiagnosticCheckRowViewModel row = category.Checks.FirstOrDefault(entry => entry.Id == result.Id);

        if (row == null)
        {
            category.Checks.Add(new DiagnosticCheckRowViewModel(result));
        }
        else
        {
            row.Result = result;
        }

        CompletedCount++;

        if (IsRunning)
        {
            HeaderText = Loc.Tr("Diagnostics_RunningFmt", CompletedCount, TotalCount);
        }
    }

    /// <summary>
    /// Keeps the results of a partial run merged into whatever the last full run produced, so
    /// re-running one check does not empty the rest of the page.
    /// </summary>
    private void Merge(DiagnosticRunResult run)
    {
        if (_lastRun == null)
        {
            _lastRun = run;
            return;
        }

        List<DiagnosticCheckResult> merged = _lastRun.Results.ToList();

        foreach (DiagnosticCheckResult result in run.Results)
        {
            int index = merged.FindIndex(entry => entry.Id == result.Id);

            if (index < 0)
            {
                merged.Add(result);
            }
            else
            {
                merged[index] = result;
            }
        }

        _lastRun = new DiagnosticRunResult(run.CompletedAt, run.WasCancelled, merged);
    }

    private void Refresh()
    {
        foreach (DiagnosticCategoryViewModel category in Categories)
        {
            category.Refresh();
        }

        IReadOnlyList<DiagnosticCheckResult> results = Categories
            .SelectMany(category => category.Checks)
            .Select(row => row.Result)
            .ToList();

        Tallies.Clear();

        foreach (DiagnosticTallyViewModel tally in DiagnosticTallyViewModel.For(results))
        {
            Tallies.Add(tally);
        }

        HeaderText = Loc.Tr("Diagnostics_HeaderFmt", results.Count,
            (_lastRun?.CompletedAt ?? DateTimeOffset.Now).ToLocalTime().ToString("HH:mm"));

        // Keep the selection, and open on the worst check the first time round.
        if (SelectedCategory == null)
        {
            SelectCategory(Categories.OrderBy(category => DiagnosticText.Rank(category.Status)).FirstOrDefault());
        }
        else if (SelectedCheck == null)
        {
            SelectCheck(SelectedCategory.FirstWorst());
        }
        else
        {
            // The row object survives a re-run, but its position and content changed.
            OnPropertyChanged(nameof(VisibleChecks));
        }
    }

    private async Task ShowReportAsync()
    {
        if (_lastRun == null)
        {
            return;
        }

        string report = DiagnosticReportBuilder.Build(_lastRun);

        await _dialogService.ShowDialogAsync<DiagnosticReportViewModel, DialogResult>(
            viewModel => viewModel.Initialize(report));
    }
}
