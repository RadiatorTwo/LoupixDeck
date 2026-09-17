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
/// Drives the Linux Diagnostics page: starts a run, shows results as they arrive, and opens the
/// report preview.
///
/// Nothing runs on its own. The page opens empty with a Run button, because one check creates
/// and destroys a virtual input device, and the issue is explicit that Device Doctor changes
/// nothing without an explicit action.
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
    }

    /// <summary>The categories, in the order their first check is registered.</summary>
    public ObservableCollection<DiagnosticCategoryViewModel> Categories { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowReportCommand))]
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

    [ObservableProperty]
    public partial string SummaryText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverallOk))]
    [NotifyPropertyChangedFor(nameof(IsOverallWarning))]
    [NotifyPropertyChangedFor(nameof(IsOverallFailed))]
    public partial DiagnosticStatus OverallStatus { get; set; } = DiagnosticStatus.Unknown;

    /// <summary>True once a finished run is available to report on.</summary>
    public bool HasResults => HasRun && !IsRunning;

    public bool IsOverallOk => HasResults && (OverallStatus == DiagnosticStatus.Pass);

    public bool IsOverallWarning => HasResults && (OverallStatus == DiagnosticStatus.Warning);

    public bool IsOverallFailed => HasResults && (OverallStatus == DiagnosticStatus.Fail);

    public IAsyncRelayCommand RunCommand => field ??= Relay.Create(RunAllAsync, () => !IsRunning);

    public IRelayCommand CancelCommand => field ??= Relay.Create(Cancel, () => IsRunning);

    public IAsyncRelayCommand ShowReportCommand => field ??= Relay.Create(ShowReportAsync, () => HasResults);

    public IAsyncRelayCommand<DiagnosticCategoryViewModel> RunCategoryCommand =>
        field ??= Relay.Create<DiagnosticCategoryViewModel>(RunCategoryAsync, category => (category != null) && !IsRunning);

    private Task RunAllAsync() => ExecuteAsync(null);

    private Task RunCategoryAsync(DiagnosticCategoryViewModel category)
        => category == null ? Task.CompletedTask : ExecuteAsync(category.Category);

    private async Task ExecuteAsync(DiagnosticCategory? category)
    {
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        CompletedCount = 0;
        TotalCount = category == null ? _diagnostics.CheckCount : _diagnostics.CountFor(category.Value);
        SummaryText = Loc.Tr("Diagnostics_RunningFmt", 0, TotalCount);

        _run = new CancellationTokenSource();

        try
        {
            // Progress<T> captures the UI SynchronizationContext, so the callback already runs
            // on the UI thread and the collections can be touched directly.
            Progress<DiagnosticCheckResult> progress = new(Apply);

            DiagnosticRunResult run = category == null
                ? await _diagnostics.RunAllAsync(progress, _run.Token)
                : await _diagnostics.RunCategoryAsync(category.Value, progress, _run.Token);

            Merge(run);
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
            RefreshSummary();
        }
    }

    private void Cancel() => _run?.Cancel();

    /// <summary>Places one finished result in its category, replacing an earlier run's row.</summary>
    private void Apply(DiagnosticCheckResult result)
    {
        DiagnosticCategoryViewModel category = Categories.FirstOrDefault(entry => entry.Category == result.Category);

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
        SummaryText = Loc.Tr("Diagnostics_RunningFmt", CompletedCount, TotalCount);
    }

    /// <summary>
    /// Keeps the results of a category run merged into whatever the last full run produced, so
    /// re-running one category does not empty the other three.
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

    private void RefreshSummary()
    {
        foreach (DiagnosticCategoryViewModel category in Categories)
        {
            category.RefreshCounts();
        }

        IReadOnlyList<DiagnosticCheckRowViewModel> rows =
            Categories.SelectMany(category => category.Checks).ToList();

        int problems = rows.Count(row => row.IsFail || row.IsWarning ||
                                         (row.Result.Status == DiagnosticStatus.Unknown));

        OverallStatus = rows.Any(row => row.IsFail)
            ? DiagnosticStatus.Fail
            : problems > 0
                ? DiagnosticStatus.Warning
                : DiagnosticStatus.Pass;

        SummaryText = problems == 0
            ? Loc.Tr("Diagnostics_SummaryAllGood")
            : Loc.Tr("Diagnostics_SummaryProblemsFmt", problems);
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
