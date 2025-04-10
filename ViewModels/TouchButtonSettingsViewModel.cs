using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using LoupixDeck.Commands.Base;
using LoupixDeck.Models;
using LoupixDeck.Services;
using LoupixDeck.Utils;

namespace LoupixDeck.ViewModels;

public class TouchButtonSettingsViewModel : ViewModelBase
{
    private readonly ObsController _obs;
    private readonly ElgatoDevices _elgatoDevices;
    public ICommand SelectImageButtonCommand { get; }
    public ICommand RemoveImageButtonCommand { get; }
    public TouchButton ButtonData { get; }

    public ObservableCollection<MenuEntry> SystemCommandMenus { get; set; }
    public MenuEntry CurrentMenuEntry { get; set; }

    private MenuEntry _elgatoKeyLightMenu;

    public TouchButtonSettingsViewModel(TouchButton buttonData,
        ObsController obs,
        ElgatoDevices elgatoDevices)
    {
        _obs = obs;
        _elgatoDevices = elgatoDevices;
        ButtonData = buttonData;

        SelectImageButtonCommand = new AsyncRelayCommand(SelectImageButton_Click);
        RemoveImageButtonCommand = new RelayCommand(RemoveImageButton_Click);

        SystemCommandMenus = new ObservableCollection<MenuEntry>();

        CreateSystemMenu();
    }

    private void CreateSystemMenu()
    {
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

    private async Task SelectImageButton_Click()
    {
        var parent = WindowHelper.GetMainWindow();
        if (parent == null) return;
        var result = await FileDialogHelper.OpenFileDialog(parent);

        if (result == null || !File.Exists(result.Path.AbsolutePath)) return;

        ButtonData.Image = new Bitmap(result.Path.AbsolutePath);
        ButtonData.RenderedImage = BitmapHelper.RenderTouchButtonContent(ButtonData, 150, 150);

        ButtonData.Refresh();
    }

    private void RemoveImageButton_Click()
    {
        ButtonData.Image = null;

        ButtonData.RenderedImage = BitmapHelper.RenderTouchButtonContent(ButtonData, 150, 150);

        ButtonData.Refresh();
    }

    public void InsertCommand(MenuEntry menuEntry)
    {
        var formattedCommand = CommandBuilder.CreateCommandFromMenuEntry(menuEntry);

        ButtonData.Command += formattedCommand;
    }
}