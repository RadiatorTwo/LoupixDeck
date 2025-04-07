using System.Collections.ObjectModel;
using LoupixDeck.Commands.Base;
using LoupixDeck.Models;
using LoupixDeck.Services;

namespace LoupixDeck.ViewModels;

public class SimpleButtonSettingsViewModel : ViewModelBase
{
    private readonly ObsController _obs;
    private readonly ElgatoDevices _elgatoDevices;

    public SimpleButton ButtonData { get; set; }
    public ObservableCollection<MenuEntry> SystemCommandMenus { get; set; }
    private MenuEntry _elgatoKeyLightMenu;
    public MenuEntry CurrentMenuEntry { get; set; }

    public SimpleButtonSettingsViewModel(SimpleButton buttonData, ObsController obs, ElgatoDevices elgatoDevices)
    {
        _obs = obs;
        _elgatoDevices = elgatoDevices;
        ButtonData = buttonData;

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
            groupMenu.Childs.Add(new MenuEntry(command.DisplayName, command.CommandName));
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

            groupMenu.Childs.Add(new MenuEntry(command.DisplayName, command.CommandName));
        }

        var scenesMenu = new MenuEntry("Scenes", string.Empty);
        var scenes = _obs.GetScenes();

        foreach (var scene in scenes)
        {
            scenesMenu.Childs.Add(new MenuEntry(scene.Name, $"System.ObsSetScene({scene.Name})"));
        }

        groupMenu.Childs.Add(scenesMenu);

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
        var checkKeyLight = _elgatoKeyLightMenu.Childs.FirstOrDefault(kl => kl.Name == keyLight.DisplayName);

        if (checkKeyLight != null)
            return;

        var keyLightGroup = new MenuEntry(keyLight.DisplayName, null);

        var commands = CommandManager.GetCommandInfos().Where(ci => ci.Group == "Elgato Keylights");

        foreach (var command in commands)
        {
            keyLightGroup.Childs.Add(new MenuEntry(command.DisplayName, command.CommandName, keyLight.DisplayName));
        }

        _elgatoKeyLightMenu.Childs.Add(keyLightGroup);
    }

    public void InsertCommand(MenuEntry menuEntry)
    {
        var formattedCommand = CommandBuilder.CreateCommandFromMenuEntry(menuEntry);

        ButtonData.Command += formattedCommand;
    }
}