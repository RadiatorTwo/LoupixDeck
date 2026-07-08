using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Models;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services;
using LoupixDeck.Services.Commands;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;
using LoupixDeck.ViewModels.CommandPicker;

namespace LoupixDeck.ViewModels;

public class SimpleButtonSettingsViewModel : DialogViewModelBase<SimpleButton, DialogResult>, IAsyncInitViewModel
{
    public override void Initialize(SimpleButton parameter)
    {
        ButtonData = parameter;

        // Remember the runtime active state and edit it; restored on Cleanup so the editor's
        // state-switching does not leave the button on a non-default state at runtime.
        _originalActiveStateId = ButtonData?.ActiveStateId ?? Guid.Empty;
        RefreshStateBadges();
        _selectedState = ButtonData?.ActiveState;

        LoadSegments();

        OnPropertyChanged(nameof(States));
        OnPropertyChanged(nameof(SelectedState));
        OnPropertyChanged(nameof(CanDeleteState));
        OnPropertyChanged(nameof(ResetOnRestart));
        OnPropertyChanged(nameof(ButtonLabel));
        OnPropertyChanged(nameof(ButtonData));
        NotifyCommandChanged();
    }

    private readonly ICommandBuilder _commandBuilder;
    private readonly IMenuTreeBuilder _menuTreeBuilder;
    private readonly ICommandRegistry _commandRegistry;

    public SimpleButton ButtonData { get; set; }

    /// <summary>Friendly label for the physical button id, displayed 1-based
    /// (BUTTON0 → "Button 1"). The underlying enum stays 0-based.</summary>
    public string ButtonLabel
    {
        get
        {
            var id = ButtonData?.Id.ToString();
            if (id != null && id.StartsWith("BUTTON") && int.TryParse(id["BUTTON".Length..], out var n))
                return $"Button {n + 1}";
            return id?.Replace("BUTTON", "Button ") ?? "Button";
        }
    }

    public ObservableCollection<MenuEntry> SystemCommandMenus { get; set; }
    public MenuEntry CurrentMenuEntry { get; set; }

    /// <summary>The card-based command picker (issue #171), projecting
    /// <see cref="SystemCommandMenus"/> into its sectioned category grid.</summary>
    public CommandPickerViewModel CommandPicker { get; }

    /// <summary>The button's command chain as individual, editable cards. The raw
    /// <see cref="LoupedeckButton.Command"/> string stays the persisted source of truth;
    /// this collection is a view over it that is recomposed on every edit.</summary>
    public ObservableCollection<CommandSegment> Commands { get; } = [];

    public IRelayCommand ClearCommandCommand => field ??= Relay.Create(ClearCommandOnly);

    /// <summary>True when the button has a non-empty command assigned.</summary>
    public bool HasCommand => !string.IsNullOrWhiteSpace(ButtonData?.Command);

    // ───────── States ─────────

    private Guid _originalActiveStateId;

    /// <summary>The button's states, shown in the States list.</summary>
    public ObservableCollection<ButtonState> States => ButtonData?.States;

    private ButtonState _selectedState;

    /// <summary>
    /// The state being edited. Selecting a state makes it the button's active state, so the LED
    /// color picker and the command sequence (which read the active state via the proxy/mirror)
    /// edit it in place.
    /// </summary>
    public ButtonState SelectedState
    {
        get => _selectedState;
        set
        {
            if (ReferenceEquals(_selectedState, value)) return;
            _selectedState = value;
            if (value != null)
                ButtonData?.SetActiveState(value.Id);

            OnPropertyChanged(nameof(SelectedState));
            OnPropertyChanged(nameof(CanDeleteState));
            OnPropertyChanged(nameof(ButtonData)); // re-read ButtonColor proxy

            // Rebuild the command-sequence cards from the now-active state's command.
            LoadSegments();
            NotifyCommandChanged();
        }
    }

    /// <summary>At least two states are needed before one can be deleted.</summary>
    public bool CanDeleteState => ButtonData?.States is { Count: > 1 };

    /// <summary>The transition kinds shown in the picker (label via TransitionKindLabelConverter).</summary>
    public IReadOnlyList<StateTransitionKind> TransitionKinds { get; } =
        (StateTransitionKind[])Enum.GetValues(typeof(StateTransitionKind));

    public bool ResetOnRestart
    {
        get => ButtonData?.ResetOnRestart ?? true;
        set
        {
            if (ButtonData == null || ButtonData.ResetOnRestart == value) return;
            ButtonData.ResetOnRestart = value;
            OnPropertyChanged(nameof(ResetOnRestart));
        }
    }

    public IRelayCommand AddStateCommand => field ??= Relay.Create(AddState);
    public IRelayCommand DuplicateStateCommand => field ??= Relay.Create(DuplicateState);
    public IRelayCommand DeleteStateCommand => field ??= Relay.Create(DeleteState);
    public IRelayCommand MoveStateUpCommand => field ??= Relay.Create(MoveStateUp);
    public IRelayCommand MoveStateDownCommand => field ??= Relay.Create(MoveStateDown);
    public IRelayCommand SetDefaultStateCommand => field ??= Relay.Create(SetDefaultStateSelected);

    private string GetUniqueStateName(string baseName)
    {
        var states = ButtonData?.States;
        if (states == null) return baseName;
        bool Exists(string name) => states.Any(s => string.Equals(s.Name, name, StringComparison.Ordinal));
        if (!Exists(baseName)) return baseName;
        var index = 1;
        while (Exists($"{baseName} {index}")) index++;
        return $"{baseName} {index}";
    }

    private void AddState()
    {
        if (ButtonData?.States == null) return;
        var state = new ButtonState { Name = GetUniqueStateName("State") };
        ButtonData.States.Add(state);
        RefreshStateBadges();
        SelectedState = state;
        OnPropertyChanged(nameof(CanDeleteState));
    }

    private static readonly Newtonsoft.Json.JsonSerializerSettings StateCloneSettings = CreateStateCloneSettings();

    private static Newtonsoft.Json.JsonSerializerSettings CreateStateCloneSettings()
    {
        var settings = new Newtonsoft.Json.JsonSerializerSettings();
        settings.Converters.Add(new Models.Converter.ColorJsonConverter());
        settings.Converters.Add(new Models.Layers.LayerJsonConverter());
        return settings;
    }

    private void DuplicateState()
    {
        if (ButtonData?.States == null || SelectedState == null) return;
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(SelectedState, StateCloneSettings);
        var clone = Newtonsoft.Json.JsonConvert.DeserializeObject<ButtonState>(json, StateCloneSettings);
        if (clone == null) return;

        clone.Id = Guid.NewGuid();
        clone.IsDefault = false;
        clone.Name = GetUniqueStateName(SelectedState.Name);

        var insertAt = ButtonData.States.IndexOf(SelectedState) + 1;
        ButtonData.States.Insert(insertAt, clone);
        RefreshStateBadges();
        SelectedState = clone;
        OnPropertyChanged(nameof(CanDeleteState));
    }

    private void DeleteState()
    {
        var states = ButtonData?.States;
        if (states is not { Count: > 1 } || SelectedState == null) return;

        var removed = SelectedState;
        var idx = states.IndexOf(removed);
        states.Remove(removed);

        if (ButtonData.DefaultStateId == removed.Id)
            ButtonData.DefaultStateId = states[0].Id;
        foreach (var s in states)
        {
            if (s.Transition?.Kind == StateTransitionKind.Specific && s.Transition.TargetStateId == removed.Id)
                s.Transition.TargetStateId = null;
        }

        RefreshStateBadges();
        SelectedState = idx < states.Count ? states[idx] : states[^1];
        OnPropertyChanged(nameof(CanDeleteState));
    }

    private void MoveStateUp()
    {
        var states = ButtonData?.States;
        if (states == null || SelectedState == null) return;
        var idx = states.IndexOf(SelectedState);
        if (idx <= 0) return;
        states.Move(idx, idx - 1);
        RefreshStateBadges();
    }

    private void MoveStateDown()
    {
        var states = ButtonData?.States;
        if (states == null || SelectedState == null) return;
        var idx = states.IndexOf(SelectedState);
        if (idx < 0 || idx >= states.Count - 1) return;
        states.Move(idx, idx + 1);
        RefreshStateBadges();
    }

    private void SetDefaultStateSelected()
    {
        if (ButtonData == null || SelectedState == null) return;
        ButtonData.DefaultStateId = SelectedState.Id;
        RefreshStateBadges();
    }

    private void RefreshStateBadges()
    {
        var states = ButtonData?.States;
        if (states == null) return;
        for (var i = 0; i < states.Count; i++)
        {
            states[i].DisplayIndex = i + 1;
            states[i].IsDefault = states[i].Id == ButtonData.DefaultStateId;
        }
    }

    public SimpleButtonSettingsViewModel(
        ICommandBuilder commandBuilder,
        IMenuTreeBuilder menuTreeBuilder,
        ICommandRegistry commandRegistry)
    {
        _commandBuilder = commandBuilder;
        _menuTreeBuilder = menuTreeBuilder;
        _commandRegistry = commandRegistry;

        // Keep the 1-based sequence numbers on the chips in sync with the
        // collection (insert, remove, move, clear, initial load).
        Commands.CollectionChanged += (_, _) => RenumberSegments();

        SystemCommandMenus = new ObservableCollection<MenuEntry>();
        CommandPicker = new CommandPickerViewModel(SystemCommandMenus);
    }

    public async Task InitializeAsync()
    {
        await _menuTreeBuilder.BuildInto(SystemCommandMenus, ButtonTargets.SimpleButton);
    }

    /// <summary>Parses <see cref="LoupedeckButton.Command"/> into editable segment cards.
    /// Does not write back — opening (and closing without edits) leaves the persisted
    /// string byte-for-byte unchanged.</summary>
    private void LoadSegments()
    {
        foreach (var segment in Commands)
            segment.Changed -= OnSegmentChanged;
        Commands.Clear();

        if (ButtonData == null) return;

        foreach (var raw in CommandStringParser.SplitChain(ButtonData.Command))
            Commands.Add(CreateSegment(raw));
    }

    /// <summary>Builds a <see cref="CommandSegment"/> from a raw segment, resolving its
    /// <see cref="Commands.Base.CommandInfo"/> when the command name is a known system command.</summary>
    private CommandSegment CreateSegment(string raw)
    {
        var name = CommandStringParser.GetName(raw);
        var info = _commandRegistry.Get(name)?.Info;
        var segment = CommandSegment.Create(_commandBuilder, info, raw);
        segment.Changed += OnSegmentChanged;
        return segment;
    }

    private void OnSegmentChanged(object sender, EventArgs e) => RebuildCommandString();

    /// <summary>Reassigns the 1-based <see cref="CommandSegment.Position"/> shown
    /// on every chip in the sequence strip.</summary>
    private void RenumberSegments()
    {
        for (var i = 0; i < Commands.Count; i++)
            Commands[i].Position = i + 1;
    }

    /// <summary>Recomposes the persisted <c>&amp;&amp;</c>-joined command string from the
    /// current card order/values. Called after every add / remove / reorder / edit.</summary>
    private void RebuildCommandString()
    {
        if (ButtonData == null) return;

        var joined = string.Join(" && ",
            Commands.Select(s => s.Raw).Where(r => !string.IsNullOrWhiteSpace(r)));

        ButtonData.Command = string.IsNullOrWhiteSpace(joined) ? null : joined;
        NotifyCommandChanged();
    }

    /// <summary>Appends a command (double-click in the tree) to the end of the chain.</summary>
    public void InsertCommand(MenuEntry menuEntry) => InsertCommandAt(menuEntry, Commands.Count);

    /// <summary>Inserts a command (drag from the tree) at the given card index.</summary>
    public void InsertCommandAt(MenuEntry menuEntry, int index)
    {
        if (ButtonData == null || menuEntry == null) return;

        var formattedCommand = _commandBuilder.CreateCommandFromMenuEntry(menuEntry);
        if (string.IsNullOrWhiteSpace(formattedCommand)) return;

        index = Math.Clamp(index, 0, Commands.Count);
        Commands.Insert(index, CreateSegment(formattedCommand));
        RebuildCommandString();
    }

    public void RemoveSegment(CommandSegment segment)
    {
        if (segment == null || !Commands.Remove(segment)) return;
        segment.Changed -= OnSegmentChanged;
        RebuildCommandString();
    }

    public void MoveSegment(int from, int to)
    {
        if (from < 0 || from >= Commands.Count) return;
        to = Math.Clamp(to, 0, Commands.Count - 1);
        if (from == to) return;
        Commands.Move(from, to);
        RebuildCommandString();
    }

    /// <summary>Clears all assigned commands — leaves the button color untouched.</summary>
    public void ClearCommandOnly()
    {
        if (ButtonData == null) return;
        foreach (var segment in Commands)
            segment.Changed -= OnSegmentChanged;
        Commands.Clear();
        ButtonData.Command = null;
        NotifyCommandChanged();
    }

    private void NotifyCommandChanged()
    {
        OnPropertyChanged(nameof(HasCommand));
    }

    /// <summary>Detaches segment handlers — called by the View when the dialog closes.</summary>
    public void Cleanup()
    {
        // Restore the runtime active state so editor state-switching is not persisted as the live
        // state (the LED repaints to it on close).
        if (ButtonData != null)
        {
            if (ButtonData.States?.Any(s => s.Id == _originalActiveStateId) == true)
                ButtonData.SetActiveState(_originalActiveStateId);
            else
                ButtonData.ResetToDefaultState();
        }

        foreach (var segment in Commands)
            segment.Changed -= OnSegmentChanged;

        CommandPicker.Cleanup();
    }
}
