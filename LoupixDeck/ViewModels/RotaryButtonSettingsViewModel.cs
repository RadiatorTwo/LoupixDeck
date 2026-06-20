using System.Collections.ObjectModel;
using LoupixDeck.Models;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services;
using LoupixDeck.Services.Commands;
using LoupixDeck.ViewModels.Base;

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

    /// <summary>Appends a command to the currently active slot (double-click in the tree).</summary>
    public void InsertCommand(MenuEntry menuEntry) => ActiveSlot?.InsertCommand(menuEntry);

    public void Cleanup()
    {
        foreach (var slot in Slots)
            slot.Cleanup();
    }
}
