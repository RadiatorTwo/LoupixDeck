using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Models;
using LoupixDeck.Models.Converter;
using LoupixDeck.Models.Macros;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services;
using LoupixDeck.Services.Commands;
using LoupixDeck.Services.Macros;
using LoupixDeck.ViewModels.Base;
using Newtonsoft.Json;

namespace LoupixDeck.ViewModels;

/// <summary>
/// ViewModel of the visual macro editor. Works on deep copies of the macros and
/// applies every valid change immediately (debounced) back into the
/// <see cref="IMacroManager"/> — there is no explicit save step.
/// </summary>
public class MacroEditorViewModel : DialogViewModelBase<DialogResult>, IAsyncInitViewModel
{
    // Serializer used for deep-cloning macros (working copies).
    private static readonly JsonSerializerSettings CloneSettings = new()
    {
        Converters = { new MacroStepJsonConverter() }
    };

    // Editor-only UI state on steps that must not trigger an auto-apply.
    private static readonly HashSet<string> NonPersistedStepProperties =
    [
        nameof(MacroStep.IsEditing),
        nameof(MacroStep.IsDragging),
        nameof(MacroStep.IsSelected),
        nameof(MacroStep.ValueText)
    ];

    private readonly IMacroManager _macroManager;
    private readonly ICommandBuilder _commandBuilder;
    private readonly IMenuTreeBuilder _menuTreeBuilder;
    private readonly MacroRunner _macroRunner;
    private readonly IInputRecorder _inputRecorder;

    // Below this gap a recorded pause is dropped as noise rather than a Delay step.
    private const int MinRecordedDelayMs = 5;

    // Debounces persisting while the user is typing; flushed on window close.
    private readonly DispatcherTimer _applyTimer;

    // Counts down before a test run so the user can focus the target window.
    private readonly DispatcherTimer _testTimer;
    private int _testCountdown;
    private CancellationTokenSource _testCts;

    // Editor-local clipboard for copy/paste of steps (deep clones, cross-macro).
    private readonly List<MacroStep> _clipboard = [];

    // Undo/redo over full editor snapshots (JSON of all macros). _currentSnapshot is the
    // last settled state; _suspendTracking silences change handlers while restoring.
    private readonly Stack<string> _undoStack = new();
    private readonly Stack<string> _redoStack = new();
    private string _currentSnapshot;
    private bool _suspendTracking;

    /// <summary>Editable working copies of all macros.</summary>
    public ObservableCollection<Macro> Macros { get; } = [];

    /// <summary>Command tree offered inside CommandStep editors.</summary>
    public ObservableCollection<MenuEntry> SystemCommandMenus { get; } = [];

    private Macro _selectedMacro;

    public Macro SelectedMacro
    {
        get => _selectedMacro;
        set
        {
            if (SetProperty(ref _selectedMacro, value))
            {
                OnPropertyChanged(nameof(HasSelectedMacro));
                OnPropertyChanged(nameof(SelectedStepCount));
                OnPropertyChanged(nameof(HasSelectedSteps));
                OnPropertyChanged(nameof(HasSelectedDelay));
                OnPropertyChanged(nameof(HasBulkActions));
                OnPropertyChanged(nameof(MacroPreview));
            }
        }
    }

    public bool HasSelectedMacro => SelectedMacro != null;

    private string _validationMessage = string.Empty;

    public string ValidationMessage
    {
        get => _validationMessage;
        private set
        {
            if (SetProperty(ref _validationMessage, value))
                OnPropertyChanged(nameof(HasValidationMessage));
        }
    }

    public bool HasValidationMessage => !string.IsNullOrEmpty(ValidationMessage);

    /// <summary>Default delay (ms) applied by the bulk "set / insert delay" actions.</summary>
    private int _bulkDelayMs = 100;

    public int BulkDelayMs
    {
        get => _bulkDelayMs;
        set => SetProperty(ref _bulkDelayMs, value);
    }

    /// <summary>Number of currently checked steps in the selected macro.</summary>
    public int SelectedStepCount =>
        SelectedMacro?.Steps.Count(s => s.IsSelected) ?? 0;

    public bool HasSelectedSteps => SelectedStepCount > 0;

    /// <summary>True when at least one selected step is a Delay (so "Set Delay" applies).</summary>
    public bool HasSelectedDelay =>
        SelectedMacro?.Steps.Any(s => s.IsSelected && s is DelayStep) ?? false;

    public bool HasClipboard => _clipboard.Count > 0;

    /// <summary>True when any bulk action row is actionable (selection present or clipboard filled).</summary>
    public bool HasBulkActions => HasSelectedSteps || HasClipboard;

    /// <summary>One-line summary of the selected macro's steps, e.g. "Type 'hi' → Ctrl+C → 100 ms".</summary>
    public string MacroPreview
    {
        get
        {
            var steps = SelectedMacro?.Steps;
            if (steps == null || steps.Count == 0)
                return string.Empty;
            return string.Join("  →  ", steps.Select(StepSummary));
        }
    }

    public bool IsTesting => _testCountdown > 0;

    private bool _isTestRunning;

    /// <summary>True while a test macro is actually executing (after the countdown).</summary>
    public bool IsTestRunning
    {
        get => _isTestRunning;
        private set
        {
            if (SetProperty(ref _isTestRunning, value))
                OnPropertyChanged(nameof(TestButtonText));
        }
    }

    public string TestButtonText =>
        IsTesting ? $"Cancel ({_testCountdown})" : IsTestRunning ? "Stop" : "Test";

    /// <summary>False on platforms without a recording backend — the button is hidden then.</summary>
    public bool IsRecordingSupported => _inputRecorder.IsSupported;

    public bool IsRecording => _inputRecorder.IsRecording;

    public string RecordButtonText => IsRecording ? "Stop Recording" : "Record";

    private bool _captureRecordedDelays = true;

    /// <summary>When on, gaps between recorded key events become Delay steps.</summary>
    public bool CaptureRecordedDelays
    {
        get => _captureRecordedDelays;
        set => SetProperty(ref _captureRecordedDelays, value);
    }

    public ICommand AddMacroCommand { get; }
    public ICommand RemoveMacroCommand { get; }
    public ICommand AddStepCommand { get; }
    public ICommand DuplicateStepCommand { get; }
    public ICommand SelectAllStepsCommand { get; }
    public ICommand ClearSelectionCommand { get; }
    public ICommand DuplicateSelectedCommand { get; }
    public ICommand CopySelectedCommand { get; }
    public ICommand PasteStepsCommand { get; }
    public ICommand DeleteSelectedCommand { get; }
    public ICommand SetDelayOnSelectedCommand { get; }
    public ICommand InsertDelayAfterSelectedCommand { get; }
    public ICommand TestMacroCommand { get; }
    public ICommand ToggleRecordingCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public MacroEditorViewModel(IMacroManager macroManager, ICommandBuilder commandBuilder,
        IMenuTreeBuilder menuTreeBuilder, MacroRunner macroRunner, IInputRecorder inputRecorder)
    {
        _macroManager = macroManager;
        _commandBuilder = commandBuilder;
        _menuTreeBuilder = menuTreeBuilder;
        _macroRunner = macroRunner;
        _inputRecorder = inputRecorder;

        foreach (var macro in macroManager.Macros)
        {
            var clone = DeepClone(macro);
            Attach(clone);
            Macros.Add(clone);
        }

        SelectedMacro = Macros.FirstOrDefault();

        Macros.CollectionChanged += Macros_CollectionChanged;

        _applyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _applyTimer.Tick += (_, _) =>
        {
            _applyTimer.Stop();
            Apply();
        };

        _testTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _testTimer.Tick += TestTimer_Tick;

        AddMacroCommand = new RelayCommand(AddMacro);
        RemoveMacroCommand = new RelayCommand(RemoveMacro);
        AddStepCommand = new RelayCommand<string>(AddStep);
        DuplicateStepCommand = new RelayCommand<MacroStep>(DuplicateStep);
        SelectAllStepsCommand = new RelayCommand(SelectAllSteps);
        ClearSelectionCommand = new RelayCommand(ClearSelection);
        DuplicateSelectedCommand = new RelayCommand(DuplicateSelected);
        CopySelectedCommand = new RelayCommand(CopySelected);
        PasteStepsCommand = new RelayCommand(PasteSteps);
        DeleteSelectedCommand = new RelayCommand(DeleteSelected);
        SetDelayOnSelectedCommand = new RelayCommand(SetDelayOnSelected);
        InsertDelayAfterSelectedCommand = new RelayCommand(InsertDelayAfterSelected);
        TestMacroCommand = new RelayCommand(ToggleTest);
        ToggleRecordingCommand = new RelayCommand(ToggleRecording);
        UndoCommand = new RelayCommand(Undo);
        RedoCommand = new RelayCommand(Redo);

        // Baseline snapshot so the first edit has something to undo back to.
        _currentSnapshot = SerializeMacros();
    }

    public async Task InitializeAsync()
    {
        // TouchButton offers the richest command set for CommandStep editors.
        await _menuTreeBuilder.BuildInto(SystemCommandMenus, ButtonTargets.TouchButton);
    }

    private void AddMacro()
    {
        var macro = new Macro { Name = GenerateMacroName() };
        Macros.Add(macro);
        SelectedMacro = macro;
    }

    private void RemoveMacro()
    {
        if (SelectedMacro == null)
            return;

        var index = Macros.IndexOf(SelectedMacro);
        Macros.Remove(SelectedMacro);
        SelectedMacro = Macros.Count > 0 ? Macros[Math.Min(index, Macros.Count - 1)] : null;
    }

    private void AddStep(string stepType)
    {
        if (SelectedMacro == null || !Enum.TryParse<MacroStepType>(stepType, out var type))
            return;

        MacroStep step = type switch
        {
            MacroStepType.Text => new TextStep(),
            MacroStepType.KeyCombination => new KeyCombinationStep(),
            MacroStepType.Delay => new DelayStep(),
            MacroStepType.KeyDown => new KeyDownStep(),
            MacroStepType.KeyUp => new KeyUpStep(),
            MacroStepType.Mouse => new MouseStep(),
            MacroStepType.Command => new CommandStep(),
            MacroStepType.RepeatStart => new RepeatStartStep(),
            MacroStepType.RepeatEnd => new RepeatEndStep(),
            _ => null
        };

        if (step == null)
            return;

        step.IsEditing = true;
        SelectedMacro.Steps.Add(step);
    }

    public void RemoveStep(MacroStep step)
    {
        SelectedMacro?.Steps.Remove(step);
    }

    /// <summary>
    /// Appends the command built from a menu entry to a CommandStep (double-click
    /// in the step's system-command tree).
    /// </summary>
    public void InsertCommandIntoStep(CommandStep step, MenuEntry menuEntry)
    {
        if (step == null || menuEntry == null)
            return;

        var formattedCommand = _commandBuilder.CreateCommandFromMenuEntry(menuEntry);
        if (string.IsNullOrEmpty(formattedCommand))
            return;

        step.CommandString = Utils.CommandChain.Append(step.CommandString, formattedCommand);
    }

    // ───────── Step bulk operations ─────────

    /// <summary>Inserts a deep copy of the step right after the original.</summary>
    public void DuplicateStep(MacroStep step)
    {
        var steps = SelectedMacro?.Steps;
        if (step == null || steps == null)
            return;

        var index = steps.IndexOf(step);
        if (index < 0)
            return;

        steps.Insert(index + 1, CloneStep(step));
    }

    private void SelectAllSteps()
    {
        if (SelectedMacro == null)
            return;
        foreach (var step in SelectedMacro.Steps)
            step.IsSelected = true;
    }

    private void ClearSelection()
    {
        if (SelectedMacro == null)
            return;
        foreach (var step in SelectedMacro.Steps)
            step.IsSelected = false;
    }

    private void DuplicateSelected()
    {
        var steps = SelectedMacro?.Steps;
        if (steps == null)
            return;

        // Iterate over a snapshot so freshly inserted clones aren't duplicated again.
        foreach (var step in Selected().ToList())
        {
            var index = steps.IndexOf(step);
            if (index < 0)
                continue;
            var clone = CloneStep(step);
            clone.IsSelected = false;
            steps.Insert(index + 1, clone);
        }
    }

    private void CopySelected()
    {
        _clipboard.Clear();
        foreach (var step in Selected())
            _clipboard.Add(CloneStep(step));
        OnPropertyChanged(nameof(HasClipboard));
        OnPropertyChanged(nameof(HasBulkActions));
    }

    /// <summary>Pastes clipboard steps after the last selected step, or at the end.</summary>
    private void PasteSteps()
    {
        var steps = SelectedMacro?.Steps;
        if (steps == null || _clipboard.Count == 0)
            return;

        var lastSelected = Selected().LastOrDefault();
        var insertAt = lastSelected != null ? steps.IndexOf(lastSelected) + 1 : steps.Count;

        foreach (var step in steps)
            step.IsSelected = false;

        foreach (var template in _clipboard)
        {
            var clone = CloneStep(template);
            clone.IsSelected = true;
            steps.Insert(insertAt++, clone);
        }
    }

    private void DeleteSelected()
    {
        var steps = SelectedMacro?.Steps;
        if (steps == null)
            return;
        foreach (var step in Selected().ToList())
            steps.Remove(step);
    }

    /// <summary>Sets the duration of every selected Delay step to <see cref="BulkDelayMs"/>.</summary>
    private void SetDelayOnSelected()
    {
        foreach (var delay in Selected().OfType<DelayStep>())
            delay.Milliseconds = BulkDelayMs;
    }

    /// <summary>Inserts a Delay step of <see cref="BulkDelayMs"/> after each selected step.</summary>
    private void InsertDelayAfterSelected()
    {
        var steps = SelectedMacro?.Steps;
        if (steps == null)
            return;

        foreach (var step in Selected().ToList())
        {
            var index = steps.IndexOf(step);
            if (index < 0)
                continue;
            steps.Insert(index + 1, new DelayStep { Milliseconds = BulkDelayMs });
        }
    }

    private IEnumerable<MacroStep> Selected() =>
        SelectedMacro?.Steps.Where(s => s.IsSelected) ?? [];

    // ───────── In-editor test playback ─────────

    private void ToggleTest()
    {
        // Counting down → cancel the countdown.
        if (IsTesting)
        {
            StopTestCountdown();
            return;
        }

        // Macro actually running → stop it.
        if (IsTestRunning)
        {
            _testCts?.Cancel();
            return;
        }

        if (SelectedMacro == null || SelectedMacro.Steps.Count == 0)
            return;

        _testCountdown = 3;
        _testTimer.Start();
        OnPropertyChanged(nameof(IsTesting));
        OnPropertyChanged(nameof(TestButtonText));
    }

    private void TestTimer_Tick(object sender, EventArgs e)
    {
        _testCountdown--;
        if (_testCountdown > 0)
        {
            OnPropertyChanged(nameof(TestButtonText));
            return;
        }

        StopTestCountdown();
        RunTest();
    }

    private void StopTestCountdown()
    {
        _testTimer.Stop();
        _testCountdown = 0;
        OnPropertyChanged(nameof(IsTesting));
        OnPropertyChanged(nameof(TestButtonText));
    }

    private async void RunTest()
    {
        // Run a clone so the live editing copy is never mutated by playback.
        var macro = DeepClone(SelectedMacro);

        _testCts = new CancellationTokenSource();
        IsTestRunning = true;
        try
        {
            await _macroRunner.Run(macro, _testCts.Token);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MacroEditor] Test run failed: {ex.Message}");
        }
        finally
        {
            IsTestRunning = false;
            _testCts.Dispose();
            _testCts = null;
        }
    }

    /// <summary>Cancels a running/counting-down test (called on window close).</summary>
    public void CancelTest()
    {
        StopTestCountdown();
        _testCts?.Cancel();
    }

    // ───────── Recording ─────────

    private void ToggleRecording()
    {
        if (!_inputRecorder.IsSupported)
            return;

        if (_inputRecorder.IsRecording)
        {
            StopRecording();
            return;
        }

        if (SelectedMacro == null)
            return;

        _inputRecorder.KeyRecorded += OnKeyRecorded;
        _inputRecorder.Start();
        OnPropertyChanged(nameof(IsRecording));
        OnPropertyChanged(nameof(RecordButtonText));
    }

    /// <summary>Stops an active recording (idempotent — safe to call on window close).</summary>
    public void StopRecording()
    {
        if (!_inputRecorder.IsRecording)
            return;

        _inputRecorder.Stop();
        _inputRecorder.KeyRecorded -= OnKeyRecorded;
        OnPropertyChanged(nameof(IsRecording));
        OnPropertyChanged(nameof(RecordButtonText));
    }

    private void OnKeyRecorded(object sender, RecordedKeyEventArgs e)
    {
        // Fired on the recorder's hook thread → marshal onto the UI thread before
        // touching the macro's step collection.
        Dispatcher.UIThread.Post(() =>
        {
            var macro = SelectedMacro;
            if (macro == null)
                return;

            var gapMs = (int)e.SinceLast.TotalMilliseconds;
            if (CaptureRecordedDelays && gapMs >= MinRecordedDelayMs)
                macro.Steps.Add(new DelayStep { Milliseconds = gapMs });

            macro.Steps.Add(e.IsDown
                ? new KeyDownStep { Key = e.KeyName }
                : new KeyUpStep { Key = e.KeyName });
        });
    }

    private static MacroStep CloneStep(MacroStep step)
    {
        var json = JsonConvert.SerializeObject(step, CloneSettings);
        return JsonConvert.DeserializeObject<MacroStep>(json, CloneSettings);
    }

    private static string StepSummary(MacroStep step) => step switch
    {
        TextStep t => $"Type \"{Truncate(t.Text)}\"",
        KeyCombinationStep k => string.IsNullOrWhiteSpace(k.Keys) ? "Keys" : k.Keys,
        DelayStep d => $"{d.Milliseconds} ms",
        KeyDownStep kd => $"↓{kd.Key}",
        KeyUpStep ku => $"↑{ku.Key}",
        RepeatStartStep rs => $"Repeat {rs.Count}× [",
        RepeatEndStep => "]",
        _ => step.ValueText is { Length: > 0 } v ? Truncate(v) : step.TypeText
    };

    private static string Truncate(string value)
    {
        value ??= string.Empty;
        return value.Length <= 20 ? value : value[..20] + "…";
    }

    // ───────── Instant apply ─────────

    private void Macros_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (Macro macro in e.OldItems)
                Detach(macro);

        if (e.NewItems != null)
            foreach (Macro macro in e.NewItems)
                Attach(macro);

        ScheduleApply();
    }

    private void Attach(Macro macro)
    {
        macro.PropertyChanged += Macro_PropertyChanged;
        macro.Steps.CollectionChanged += Steps_CollectionChanged;
        foreach (var step in macro.Steps)
            step.PropertyChanged += Step_PropertyChanged;
    }

    private void Detach(Macro macro)
    {
        macro.PropertyChanged -= Macro_PropertyChanged;
        macro.Steps.CollectionChanged -= Steps_CollectionChanged;
        foreach (var step in macro.Steps)
            step.PropertyChanged -= Step_PropertyChanged;
    }

    private void Macro_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Macro.Name))
            ScheduleApply();
    }

    private void Steps_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (MacroStep step in e.OldItems)
                step.PropertyChanged -= Step_PropertyChanged;

        if (e.NewItems != null)
            foreach (MacroStep step in e.NewItems)
                step.PropertyChanged += Step_PropertyChanged;

        RefreshSelectionState();
        OnPropertyChanged(nameof(MacroPreview));
        ScheduleApply();
    }

    private void Step_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MacroStep.IsSelected))
        {
            RefreshSelectionState();
            return;
        }

        // Any value change (the data property fires alongside ValueText) refreshes the preview.
        if (e.PropertyName != nameof(MacroStep.IsEditing) &&
            e.PropertyName != nameof(MacroStep.IsDragging))
            OnPropertyChanged(nameof(MacroPreview));

        if (!NonPersistedStepProperties.Contains(e.PropertyName))
            ScheduleApply();
    }

    private void RefreshSelectionState()
    {
        OnPropertyChanged(nameof(SelectedStepCount));
        OnPropertyChanged(nameof(HasSelectedSteps));
        OnPropertyChanged(nameof(HasSelectedDelay));
        OnPropertyChanged(nameof(HasBulkActions));
    }

    /// <summary>
    /// Validates immediately (for instant feedback at the name box) and schedules a
    /// debounced apply. Invalid working sets are never pushed to the manager — the
    /// last valid state stays persisted until the input is fixed.
    /// </summary>
    private void ScheduleApply()
    {
        // Restoring a snapshot must not feed its own mutations back into the history.
        if (_suspendTracking)
            return;

        _applyTimer.Stop();

        if (!Validate())
            return;

        _applyTimer.Start();
    }

    private void Apply()
    {
        // Push clones so the manager never shares instances with the editor's working set.
        _macroManager.ReplaceAll(Macros.Select(DeepClone));
        RecordSnapshot();
    }

    /// <summary>Persists a pending (debounced) apply. Called when the editor window closes.</summary>
    public void FlushPendingChanges()
    {
        if (!_applyTimer.IsEnabled)
            return;

        _applyTimer.Stop();

        if (Validate())
            Apply();
    }

    // ───────── Undo / redo ─────────

    private void RecordSnapshot()
    {
        var snapshot = SerializeMacros();
        if (snapshot == _currentSnapshot)
            return;

        _undoStack.Push(_currentSnapshot);
        _redoStack.Clear();
        _currentSnapshot = snapshot;
        RaiseUndoRedoState();
    }

    private void Undo()
    {
        SettlePending();
        if (_undoStack.Count == 0)
            return;

        _redoStack.Push(_currentSnapshot);
        _currentSnapshot = _undoStack.Pop();
        RestoreSnapshot(_currentSnapshot);
        RaiseUndoRedoState();
    }

    private void Redo()
    {
        SettlePending();
        if (_redoStack.Count == 0)
            return;

        _undoStack.Push(_currentSnapshot);
        _currentSnapshot = _redoStack.Pop();
        RestoreSnapshot(_currentSnapshot);
        RaiseUndoRedoState();
    }

    /// <summary>Flushes a debounced edit into history so undo steps back from it, not over it.</summary>
    private void SettlePending()
    {
        if (!_applyTimer.IsEnabled)
            return;

        _applyTimer.Stop();
        if (Validate())
            Apply();
    }

    /// <summary>Rebuilds the working macros from a snapshot and re-syncs manager + selection.</summary>
    private void RestoreSnapshot(string snapshot)
    {
        var selectedName = SelectedMacro?.Name;

        _suspendTracking = true;
        Macros.CollectionChanged -= Macros_CollectionChanged;
        try
        {
            foreach (var macro in Macros)
                Detach(macro);
            Macros.Clear();

            foreach (var macro in DeserializeMacros(snapshot))
            {
                Attach(macro);
                Macros.Add(macro);
            }
        }
        finally
        {
            Macros.CollectionChanged += Macros_CollectionChanged;
            _suspendTracking = false;
        }

        SelectedMacro = Macros.FirstOrDefault(m => m.Name == selectedName) ?? Macros.FirstOrDefault();

        // Mirror the restored state into the manager (bypassing the debounce).
        _macroManager.ReplaceAll(Macros.Select(DeepClone));

        OnPropertyChanged(nameof(MacroPreview));
        RefreshSelectionState();
        ValidationMessage = string.Empty;
    }

    private void RaiseUndoRedoState()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private string SerializeMacros() => JsonConvert.SerializeObject(Macros, CloneSettings);

    private static IEnumerable<Macro> DeserializeMacros(string json) =>
        JsonConvert.DeserializeObject<List<Macro>>(json, CloneSettings) ?? [];

    private bool Validate()
    {
        foreach (var macro in Macros)
        {
            if (!MacroManager.HasValidNameCharacters(macro.Name))
            {
                ValidationMessage =
                    $"Invalid macro name '{macro.Name}': names must not be empty or contain ( ) , &";
                return false;
            }
        }

        var duplicate = Macros
            .GroupBy(m => m.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicate != null)
        {
            ValidationMessage = $"Duplicate macro name '{duplicate.Key}'.";
            return false;
        }

        ValidationMessage = string.Empty;
        return true;
    }

    private string GenerateMacroName()
    {
        const string baseName = "New Macro";
        var name = baseName;
        var counter = 2;
        while (Macros.Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)))
            name = $"{baseName} {counter++}";
        return name;
    }

    private static Macro DeepClone(Macro macro)
    {
        var json = JsonConvert.SerializeObject(macro, CloneSettings);
        return JsonConvert.DeserializeObject<Macro>(json, CloneSettings);
    }
}
