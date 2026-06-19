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

    // Debounces persisting while the user is typing; flushed on window close.
    private readonly DispatcherTimer _applyTimer;

    // Counts down before a test run so the user can focus the target window.
    private readonly DispatcherTimer _testTimer;
    private int _testCountdown;

    // Editor-local clipboard for copy/paste of steps (deep clones, cross-macro).
    private readonly List<MacroStep> _clipboard = [];

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

    public string TestButtonText => IsTesting ? $"Cancel ({_testCountdown})" : "Test";

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

    public MacroEditorViewModel(IMacroManager macroManager, ICommandBuilder commandBuilder,
        IMenuTreeBuilder menuTreeBuilder, MacroRunner macroRunner)
    {
        _macroManager = macroManager;
        _commandBuilder = commandBuilder;
        _menuTreeBuilder = menuTreeBuilder;
        _macroRunner = macroRunner;

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

    /// <summary>Inserts a Delay step after each selected step (delays between actions).</summary>
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
        if (IsTesting)
        {
            StopTestCountdown();
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

    private void RunTest()
    {
        // Run a clone so the live editing copy is never mutated by playback.
        var macro = DeepClone(SelectedMacro);
        _ = _macroRunner.Run(macro);
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
        OnPropertyChanged(nameof(HasBulkActions));
    }

    /// <summary>
    /// Validates immediately (for instant feedback at the name box) and schedules a
    /// debounced apply. Invalid working sets are never pushed to the manager — the
    /// last valid state stays persisted until the input is fixed.
    /// </summary>
    private void ScheduleApply()
    {
        _applyTimer.Stop();

        if (!Validate())
            return;

        _applyTimer.Start();
    }

    private void Apply()
    {
        // Push clones so the manager never shares instances with the editor's working set.
        _macroManager.ReplaceAll(Macros.Select(DeepClone));
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
