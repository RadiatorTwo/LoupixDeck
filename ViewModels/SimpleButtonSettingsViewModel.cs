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

    public SimpleButtonSettingsViewModel(ICommandBuilder commandBuilder, IMenuTreeBuilder menuTreeBuilder)
    {
        _commandBuilder = commandBuilder;
        _menuTreeBuilder = menuTreeBuilder;

        SystemCommandMenus = new ObservableCollection<MenuEntry>();
    }

    public async Task InitializeAsync()
    {
        await _menuTreeBuilder.BuildInto(SystemCommandMenus, ButtonTargets.SimpleButton);
    }

    public void InsertCommand(MenuEntry menuEntry)
    {
        var formattedCommand = _commandBuilder.CreateCommandFromMenuEntry(menuEntry);
        ButtonData.Command = Utils.CommandChain.Append(ButtonData.Command, formattedCommand);
    }
}
