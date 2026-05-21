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
    }

    /// <summary>User-facing label, 0-based to match the System.UpdateButton
    /// / GotoRotaryPage index space.</summary>
    public string KnobLabel => $"Rotary Button {ButtonData?.Index ?? 0}";

    private readonly ICommandBuilder _commandBuilder;
    private readonly IMenuTreeBuilder _menuTreeBuilder;

    public RotaryButton ButtonData { get; set; }

    public ObservableCollection<MenuEntry> SystemCommandMenus { get; set; }
    public MenuEntry CurrentMenuEntry { get; set; }

    public RotaryButtonSettingsViewModel(ICommandBuilder commandBuilder, IMenuTreeBuilder menuTreeBuilder)
    {
        _commandBuilder = commandBuilder;
        _menuTreeBuilder = menuTreeBuilder;

        SystemCommandMenus = new ObservableCollection<MenuEntry>();
    }

    public async Task InitializeAsync()
    {
        var menus = await _menuTreeBuilder.Build(ButtonTargets.RotaryEncoder);
        foreach (var menu in menus)
            SystemCommandMenus.Add(menu);
    }

    public enum SelectedCommand
    {
        RotaryLeft,
        RotaryRight,
        ButtonPress
    }

    public void InsertCommand(MenuEntry menuEntry, SelectedCommand selection)
    {
        var formattedCommand = _commandBuilder.CreateCommandFromMenuEntry(menuEntry);

        switch (selection)
        {
            case SelectedCommand.RotaryLeft:
                ButtonData.RotaryLeftCommand = Utils.CommandChain.Append(ButtonData.RotaryLeftCommand, formattedCommand);
                break;
            case SelectedCommand.RotaryRight:
                ButtonData.RotaryRightCommand = Utils.CommandChain.Append(ButtonData.RotaryRightCommand, formattedCommand);
                break;
            case SelectedCommand.ButtonPress:
                ButtonData.Command = Utils.CommandChain.Append(ButtonData.Command, formattedCommand);
                break;
        }
    }
}
