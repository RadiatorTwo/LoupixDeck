using System.Collections.ObjectModel;
using LoupixDeck.Commands.Base;
using LoupixDeck.Models;
using LoupixDeck.Services;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.ViewModels;

public class SimpleButtonSettingsViewModel : DialogViewModelBase<SimpleButton, DialogResult>, IAsyncInitViewModel
{
    public override void Initialize(SimpleButton parameter)
    {
        ButtonData = parameter;
    }
    
    private readonly IObsController _obs;
    private readonly ElgatoDevices _elgatoDevices;
    private readonly ISysCommandService _sysCommandService;
    private readonly ICommandBuilder _commandBuilder;

    public SimpleButton ButtonData { get; set; }
    public ObservableCollection<MenuEntry> SystemCommandMenus { get; set; }
    private MenuEntry _elgatoKeyLightMenu;
    public MenuEntry CurrentMenuEntry { get; set; }

    public SimpleButtonSettingsViewModel(IObsController obs, ElgatoDevices elgatoDevices,
        ISysCommandService sysCommandService, ICommandBuilder commandBuilder)
    {
        _obs = obs;
        _elgatoDevices = elgatoDevices;
        _sysCommandService = sysCommandService;
        _commandBuilder = commandBuilder;
    }
    
    public Task InitializeAsync()
    {
        return CreateSystemMenu();
    }

    private async Task CreateSystemMenu()
    {
        SystemCommandMenus = new ObservableCollection<MenuEntry>();
        CreatePagesMenu();
        await CreateObsMenu();
        CreateElgatoMenu();
    }

    private void CreatePagesMenu()
    {
        // Get Only Pages Commands
        var commands = _sysCommandService.GetCommandInfos().Where(ci => ci.Group == "Pages");

        var groupMenu = new MenuEntry("Pages", string.Empty);

        foreach (var command in commands)
        {
            groupMenu.Children.Add(new MenuEntry(command.DisplayName, command.CommandName));
        }

        SystemCommandMenus.Add(groupMenu);
    }

    private async Task CreateObsMenu()
    {
        var commands = _sysCommandService.GetCommandInfos().Where(ci => ci.Group == "OBS");

        var groupMenu = new MenuEntry("OBS", string.Empty);

        foreach (var command in commands)
        {
            if (command.CommandName == "System.ObsSetScene")
                continue;

            groupMenu.Children.Add(new MenuEntry(command.DisplayName, command.CommandName));
        }

        var scenesMenu = new MenuEntry("Scenes", string.Empty);
        var scenes = await _obs.GetScenes();

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

        var commands = _sysCommandService.GetCommandInfos().Where(ci => ci.Group == "Elgato Keylights");

        foreach (var command in commands)
        {
            keyLightGroup.Children.Add(new MenuEntry(command.DisplayName, command.CommandName, keyLight.DisplayName));
        }

        _elgatoKeyLightMenu.Children.Add(keyLightGroup);
    }

    public void InsertCommand(MenuEntry menuEntry)
    {
        var formattedCommand = _commandBuilder.CreateCommandFromMenuEntry(menuEntry);

        ButtonData.Command += formattedCommand;
    }
}