using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Localization;
using LoupixDeck.Models;
using LoupixDeck.PluginSdk;
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
    private readonly IInteractiveDiagnosticTests _tests;
    private readonly ICommandService _commands;

    private CancellationTokenSource _run;
    private DiagnosticRunResult _lastRun;

    public LinuxDiagnosticsViewModel(ILinuxDiagnosticsService diagnostics, IDialogService dialogService,
        IInteractiveDiagnosticTests tests, ICommandService commands)
    {
        _diagnostics = diagnostics;
        _dialogService = dialogService;
        _tests = tests;
        _commands = commands;
        Categories = [];
        Tallies = [];
    }

    /// <summary>The category column, in the order the checks are registered.</summary>
    public ObservableCollection<DiagnosticCategoryViewModel> Categories { get; }

    /// <summary>The counter pills in the header.</summary>
    public ObservableCollection<DiagnosticTallyViewModel> Tallies { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleChecks))]
    [NotifyCanExecuteChangedFor(nameof(RerunCategoryCommand))]
    public partial DiagnosticCategoryViewModel SelectedCategory { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(RerunCheckCommand))]
    public partial DiagnosticCheckRowViewModel SelectedCheck { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowReportCommand))]
    [NotifyCanExecuteChangedFor(nameof(RerunCheckCommand))]
    [NotifyCanExecuteChangedFor(nameof(RerunCategoryCommand))]
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

    public IAsyncRelayCommand RerunCategoryCommand =>
        field ??= Relay.Create(RerunCategoryAsync, () => !IsRunning && (SelectedCategory != null));

    /// <summary>
    /// The optional injection test. It is the only action of the page that changes anything on
    /// the system, so it asks first and says exactly what it will do.
    /// </summary>
    public IAsyncRelayCommand KeyTestCommand =>
        field ??= Relay.Create(RunKeyTestAsync, () => !IsRunning);

    /// <summary>The interactive recording test: the user presses a key, the app reports it.</summary>
    public IAsyncRelayCommand RecordingTestCommand =>
        field ??= Relay.Create(RunRecordingTestAsync, () => !IsRunning);

    /// <summary>
    /// Starts the display test pattern on this device. The test itself is the existing
    /// System.DisplayTest command - the diagnostics link to it rather than drawing their own
    /// patterns, so there is one implementation of what a correct display looks like.
    /// </summary>
    public IAsyncRelayCommand DisplayTestCommand =>
        field ??= Relay.Create(RunDisplayTestAsync, () => !IsRunning);

    /// <summary>The online store test. The only action of the page that leaves the machine.</summary>
    public IAsyncRelayCommand StoreTestCommand =>
        field ??= Relay.Create(RunStoreTestAsync, () => !IsRunning);

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

    /// <summary>
    /// Re-runs the selected category only. After a repair that is what the user wants: the
    /// checks the fix was about, not the uinput probe and the evdev sweep all over again.
    /// </summary>
    private Task RerunCategoryAsync()
    {
        DiagnosticCategoryViewModel category = SelectedCategory;

        return category == null
            ? Task.CompletedTask
            : ExecuteAsync(() => _diagnostics.RunCategoryAsync(category.Category, BuildProgress(), _run.Token),
                _diagnostics.CountFor(category.Category));
    }

    private async Task RunKeyTestAsync()
    {
        bool confirmed = await ConfirmAsync(Loc.Tr("Diagnostics_KeyTestConfirm"),
            Loc.Tr("Diagnostics_KeyTestTitle"));

        if (!confirmed)
        {
            return;
        }

        InteractiveTestResult result = await _tests.SendTestKeyAsync(CancellationToken.None);

        await NotifyAsync(Loc.Tr("Diagnostics_KeyTestTitle"), result);
        await RerunCategoryIfSelectedAsync(DiagnosticCategory.InputInjection);
    }

    private async Task RunRecordingTestAsync()
    {
        bool confirmed = await ConfirmAsync(Loc.Tr("Diagnostics_RecordingTestConfirm"),
            Loc.Tr("Diagnostics_RecordingTestTitle"));

        if (!confirmed)
        {
            return;
        }

        InteractiveTestResult result =
            await _tests.AwaitKeyEventAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

        await NotifyAsync(Loc.Tr("Diagnostics_RecordingTestTitle"), result);
        await RerunCategoryIfSelectedAsync(DiagnosticCategory.InputRecording);
    }

    private async Task RunDisplayTestAsync()
    {
        bool confirmed = await ConfirmAsync(Loc.Tr("Diagnostics_DisplayTestConfirm"),
            Loc.Tr("Diagnostics_DisplayTestTitle"));

        if (!confirmed)
        {
            return;
        }

        // Takes the display over until a key is pressed, exactly as the command does when a
        // button runs it.
        await _commands.ExecuteCommand("System.DisplayTest(cycle,5)", ButtonTargets.None);
    }

    private async Task RunStoreTestAsync()
    {
        bool confirmed = await ConfirmAsync(Loc.Tr("Diagnostics_StoreTestConfirm"),
            Loc.Tr("Diagnostics_StoreTestTitle"));

        if (!confirmed)
        {
            return;
        }

        InteractiveTestResult result = await _tests.CheckPluginStoreAsync(CancellationToken.None);

        await NotifyAsync(Loc.Tr("Diagnostics_StoreTestTitle"), result);
    }

    /// <summary>After a test, only the category it belongs to is worth running again.</summary>
    private Task RerunCategoryIfSelectedAsync(DiagnosticCategory category)
        => ExecuteAsync(() => _diagnostics.RunCategoryAsync(category, BuildProgress(), _run.Token),
            _diagnostics.CountFor(category));

    private async Task<bool> ConfirmAsync(string message, string title)
    {
        DialogResult result = await _dialogService.ShowDialogAsync<ConfirmDialogViewModel, DialogResult>(
            viewModel => viewModel.Configure(message, title, Loc.Tr("Diagnostics_TestStart"),
                Loc.Tr("Confirm_No")));

        return result?.IsConfirmed == true;
    }

    private Task NotifyAsync(string title, InteractiveTestResult result)
    {
        string message = string.IsNullOrWhiteSpace(result.Detail)
            ? result.Message
            : $"{result.Message}\n\n{result.Detail}";

        return _dialogService.ShowDialogAsync<ConfirmDialogViewModel, DialogResult>(
            viewModel => viewModel.Configure(message, title, Loc.Tr("Confirm_Ok"), showCancel: false));
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
