using System.Collections.ObjectModel;
using LoupixDeck.Models;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services;
using LoupixDeck.Services.Commands;
using LoupixDeck.ViewModels.Base;
using LoupixDeck.ViewModels.CommandPicker;

namespace LoupixDeck.ViewModels;

public class RotaryButtonSettingsViewModel : DialogViewModelBase<RotaryButton, DialogResult>, IAsyncInitViewModel
{
    public override void Initialize(RotaryButton parameter)
    {
        ButtonData = parameter;

        RotaryLeftSlot = new CommandSequenceSlot("Rotate Left", _commandBuilder, _commandRegistry,
            () => ButtonData.RotaryLeftCommand, v => ButtonData.RotaryLeftCommand = v);
        RotaryRightSlot = new CommandSequenceSlot("Rotate Right", _commandBuilder, _commandRegistry,
            () => ButtonData.RotaryRightCommand, v => ButtonData.RotaryRightCommand = v);
        ButtonPressSlot = new CommandSequenceSlot("Button Press", _commandBuilder, _commandRegistry,
            () => ButtonData.Command, v => ButtonData.Command = v);

        Slots = [RotaryLeftSlot, RotaryRightSlot, ButtonPressSlot];

        // The first slot is the default double-click target.
        SetActiveSlot(RotaryLeftSlot);

        OnPropertyChanged(nameof(KnobLabel));
        OnPropertyChanged(nameof(RotaryLeftSlot));
        OnPropertyChanged(nameof(RotaryRightSlot));
        OnPropertyChanged(nameof(ButtonPressSlot));
        OnPropertyChanged(nameof(Slots));
    }

    /// <summary>User-facing label. Displayed 1-based so the first knob reads
    /// "Rotary Button 1"; the underlying Index stays 0-based to match the
    /// System.UpdateButton / GotoRotaryPage index space.</summary>
    public string KnobLabel => $"Rotary Button {(ButtonData?.Index ?? 0) + 1}";

    private readonly ICommandBuilder _commandBuilder;
    private readonly IMenuTreeBuilder _menuTreeBuilder;
    private readonly ICommandRegistry _commandRegistry;

    public RotaryButton ButtonData { get; set; }

    public ObservableCollection<MenuEntry> SystemCommandMenus { get; set; }
    public MenuEntry CurrentMenuEntry { get; set; }

    /// <summary>The card-based command picker (issue #171).</summary>
    public CommandPickerViewModel CommandPicker { get; }

    /// <summary>The three command sequences of a rotary encoder: left turn, right
    /// turn and the knob press. Each is an independent, editable chip pipeline.</summary>
    public CommandSequenceSlot RotaryLeftSlot { get; private set; }
    public CommandSequenceSlot RotaryRightSlot { get; private set; }
    public CommandSequenceSlot ButtonPressSlot { get; private set; }

    public IReadOnlyList<CommandSequenceSlot> Slots { get; private set; } = [];

    /// <summary>The slot that a double-clicked command in the tree is appended to.
    /// Set by clicking a sequence strip in the view.</summary>
    public CommandSequenceSlot ActiveSlot { get; private set; }

    public RotaryButtonSettingsViewModel(
        ICommandBuilder commandBuilder,
        IMenuTreeBuilder menuTreeBuilder,
        ICommandRegistry commandRegistry)
    {
        _commandBuilder = commandBuilder;
        _menuTreeBuilder = menuTreeBuilder;
        _commandRegistry = commandRegistry;

        SystemCommandMenus = new ObservableCollection<MenuEntry>();
        CommandPicker = new CommandPickerViewModel(SystemCommandMenus);
    }

    public async Task InitializeAsync()
    {
        await _menuTreeBuilder.BuildInto(SystemCommandMenus, ButtonTargets.RotaryEncoder);
    }

    /// <summary>Marks <paramref name="slot"/> as the active double-click target and
    /// clears the highlight on the others.</summary>
    public void SetActiveSlot(CommandSequenceSlot slot)
    {
        if (slot == null || ReferenceEquals(ActiveSlot, slot)) return;

        ActiveSlot = slot;
        foreach (var s in Slots)
            s.IsActive = ReferenceEquals(s, slot);

        OnPropertyChanged(nameof(ActiveSlot));
    }

    /// <summary>Handles a double-clicked tree entry: a command group fills all its
    /// mapped slots at once, any other entry is appended to the active slot.</summary>
    public void InsertCommand(MenuEntry menuEntry)
    {
        if (menuEntry?.RotaryGroup is { Count: > 0 } group)
        {
            ApplyGroup(group);
            return;
        }

        ActiveSlot?.InsertCommand(menuEntry);
    }

    /// <summary>Applies a command group: each rotary action replaces the command of
    /// its matching slot. Actions absent from the map leave their slot untouched.</summary>
    private void ApplyGroup(IReadOnlyDictionary<RotaryAction, string> group)
    {
        foreach (var (action, raw) in group)
        {
            var slot = action switch
            {
                RotaryAction.CounterClockwise => RotaryLeftSlot,
                RotaryAction.Clockwise => RotaryRightSlot,
                RotaryAction.Press => ButtonPressSlot,
                _ => null
            };

            slot?.SetCommand(raw);
        }
    }

    public void Cleanup()
    {
        foreach (var slot in Slots)
            slot.Cleanup();

        CommandPicker.Cleanup();
    }
}