using System.Collections.ObjectModel;
using LoupixDeck.Commands.Base;
using LoupixDeck.Models;
using LoupixDeck.Services;

namespace LoupixDeck.ViewModels;

public class RotaryButtonSettingsViewModel : ViewModelBase
{
    private readonly ObsController _obs;
    private readonly ElgatoDevices _elgatoDevices;

    public RotaryButton ButtonData { get; set; }

    public ObservableCollection<MenuEntry> SystemCommandMenus { get; set; }
    private MenuEntry _elgatoKeyLightMenu;
    public MenuEntry CurrentMenuEntry { get; set; }

    public RotaryButtonSettingsViewModel(RotaryButton buttonData, ObsController obs, ElgatoDevices elgatoDevices)
    {
        ButtonData = buttonData;
        _obs = obs;
        _elgatoDevices = elgatoDevices;

        CreateSystemMenu();
    }

    private void CreateSystemMenu()
    {
        SystemCommandMenus = new ObservableCollection<MenuEntry>();
        CreatePagesMenu();
        CreateObsMenu();
        CreateElgatoMenu();
    }

    private void CreatePagesMenu()
    {
        // Get Only Pages Commands
        var commands = CommandManager.GetCommandInfos().Where(ci => ci.Group == "Pages");

        var groupMenu = new MenuEntry("Pages", string.Empty);

        foreach (var command in commands)
        {
            groupMenu.Children.Add(new MenuEntry(command.DisplayName, command.CommandName));
        }

        SystemCommandMenus.Add(groupMenu);
    }

    private void CreateObsMenu()
    {
        var commands = CommandManager.GetCommandInfos().Where(ci => ci.Group == "OBS");

        var groupMenu = new MenuEntry("OBS", string.Empty);

        foreach (var command in commands)
        {
            if (command.CommandName == "System.ObsSetScene")
                continue;

            groupMenu.Children.Add(new MenuEntry(command.DisplayName, command.CommandName));
        }

        var scenesMenu = new MenuEntry("Scenes", string.Empty);
        var scenes = _obs.GetScenes();

        foreach (var scene in scenes)
        {
            scenesMenu.Children.Add(new MenuEntry(scene.Name, $"System.ObsSetScene({scene.Name})"));
        }

        groupMenu.Children.Add(scenesMenu);

        SystemCommandMenus.Add(groupMenu);
    }

    private void CreateElgatoMenu()
    {
        _elgatoKeyLightMenu = new MenuEntry("Elgato Keylights", string.Empty);

        foreach (var keyLight in _elgatoDevices.KeyLights)
        {
            AddKeyLightMenuEntry(keyLight);
        }

        _elgatoDevices.KeyLightAdded += KeyLightAdded;

        SystemCommandMenus.Add(_elgatoKeyLightMenu);
    }

    private void KeyLightAdded(object sender, KeyLight e)
    {
        AddKeyLightMenuEntry(e);
    }

    private void AddKeyLightMenuEntry(KeyLight keyLight)
    {
        var checkKeyLight = _elgatoKeyLightMenu.Children.FirstOrDefault(kl => kl.Name == keyLight.DisplayName);

        if (checkKeyLight != null)
            return;

        var keyLightGroup = new MenuEntry(keyLight.DisplayName, null);

        var commands = CommandManager.GetCommandInfos().Where(ci => ci.Group == "Elgato Keylights");

        foreach (var command in commands)
        {
            keyLightGroup.Children.Add(new MenuEntry(command.DisplayName, command.CommandName, keyLight.DisplayName));
        }

        _elgatoKeyLightMenu.Children.Add(keyLightGroup);
    }

    public enum SelectedCommand
    {
        RotaryLeft,
        RotaryRight,
        ButtonPress
    }

    public void InsertCommand(MenuEntry menuEntry, SelectedCommand selection)
    {
        var formattedCommand = CommandBuilder.CreateCommandFromMenuEntry(menuEntry);

        switch (selection)
        {
            case SelectedCommand.RotaryLeft:
                ButtonData.RotaryLeftCommand += formattedCommand;
                break;
            case SelectedCommand.RotaryRight:
                ButtonData.RotaryRightCommand += formattedCommand;
                break;
            case SelectedCommand.ButtonPress:
                ButtonData.Command += formattedCommand;
                break;
        }
    }
}