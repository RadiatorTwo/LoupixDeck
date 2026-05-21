using System.Collections.ObjectModel;
using LoupixDeck.Models;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services;
using LoupixDeck.Services.Commands;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels;

public class SimpleButtonSettingsViewModel : DialogViewModelBase<SimpleButton, DialogResult>, IAsyncInitViewModel
{
    public override void Initialize(SimpleButton parameter)
    {
        ButtonData = parameter;
    }

    private readonly ICommandBuilder _commandBuilder;
    private readonly IMenuTreeBuilder _menuTreeBuilder;

    public SimpleButton ButtonData { get; set; }

    /// <summary>Friendly label for the physical button id (BUTTON0 → "Button 0").</summary>
    public string ButtonLabel => ButtonData?.Id.ToString().Replace("BUTTON", "Button ") ?? "Button";
    public ObservableCollection<MenuEntry> SystemCommandMenus { get; set; }
    public MenuEntry CurrentMenuEntry { get; set; }

    public SimpleButtonSettingsViewModel(ICommandBuilder commandBuilder, IMenuTreeBuilder menuTreeBuilder)
    {
        _commandBuilder = commandBuilder;
        _menuTreeBuilder = menuTreeBuilder;

        SystemCommandMenus = new ObservableCollection<MenuEntry>();
    }

    public async Task InitializeAsync()
    {
        var menus = await _menuTreeBuilder.Build(ButtonTargets.SimpleButton);
        foreach (var menu in menus)
            SystemCommandMenus.Add(menu);
    }

    public void InsertCommand(MenuEntry menuEntry)
    {
        var formattedCommand = _commandBuilder.CreateCommandFromMenuEntry(menuEntry);
        ButtonData.Command = Utils.CommandChain.Append(ButtonData.Command, formattedCommand);
    }
}
