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
        nameof(MacroStep.ValueText)
    ];

    private readonly IMacroManager _macroManager;
    private readonly ICommandBuilder _commandBuilder;
    private readonly IMenuTreeBuilder _menuTreeBuilder;

    // Debounces persisting while the user is typing; flushed on window close.
    private readonly DispatcherTimer _applyTimer;

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
                OnPropertyChanged(nameof(HasSelectedMacro));
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

    public ICommand AddMacroCommand { get; }
    public ICommand RemoveMacroCommand { get; }
    public ICommand AddStepCommand { get; }

    public MacroEditorViewModel(IMacroManager macroManager, ICommandBuilder commandBuilder,
        IMenuTreeBuilder menuTreeBuilder)
    {
        _macroManager = macroManager;
        _commandBuilder = commandBuilder;
        _menuTreeBuilder = menuTreeBuilder;

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

        AddMacroCommand = new RelayCommand(AddMacro);
        RemoveMacroCommand = new RelayCommand(RemoveMacro);
        AddStepCommand = new RelayCommand<string>(AddStep);
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

        ScheduleApply();
    }

    private void Step_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (!NonPersistedStepProperties.Contains(e.PropertyName))
            ScheduleApply();
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
