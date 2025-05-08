using System.Collections.ObjectModel;
using System.Windows.Input;
using LoupixDeck.Models;
using LoupixDeck.Services;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels.Base;
using SkiaSharp;

namespace LoupixDeck.ViewModels;

public class TouchButtonSettingsViewModel : DialogViewModelBase<TouchButton, DialogResult>

{
    public override void Initialize(TouchButton parameter)
    {
        ButtonData = parameter;
    }

    private readonly IObsController _obs;
    private readonly ElgatoDevices _elgatoDevices;
    private readonly ISysCommandService _sysCommandService;
    private readonly ICommandBuilder _commandBuilder;

    public ICommand SelectImageButtonCommand { get; }
    public ICommand RemoveImageButtonCommand { get; }
    public TouchButton ButtonData { get; set; }

    public ObservableCollection<MenuEntry> SystemCommandMenus { get; set; }
    public MenuEntry CurrentMenuEntry { get; set; }

    private MenuEntry _elgatoKeyLightMenu;

    public TouchButtonSettingsViewModel(IObsController obs,
        ElgatoDevices elgatoDevices,
        ISysCommandService sysCommandService,
        ICommandBuilder commandBuilder)
    {
        _obs = obs;
        _elgatoDevices = elgatoDevices;
        _sysCommandService = sysCommandService;
        _commandBuilder = commandBuilder;

        SelectImageButtonCommand = new AsyncRelayCommand(SelectImageButton_Click);
        RemoveImageButtonCommand = new RelayCommand(RemoveImageButton_Click);

        SystemCommandMenus = new ObservableCollection<MenuEntry>();

        CreateSystemMenu();
    }

    private void CreateSystemMenu()
    {
        CreatePagesMenu();
        CreateMacroMenu();
        CreateObsMenu();
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

    private void CreateObsMenu()
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
        var scenes = _obs.GetScenes();

        foreach (var scene in scenes)
        {
            scenesMenu.Children.Add(new MenuEntry(scene.Name, $"System.ObsSetScene"));
        }

        groupMenu.Children.Add(scenesMenu);

        SystemCommandMenus.Add(groupMenu);
    }

    private void CreateMacroMenu()
    {
        var commands = _sysCommandService.GetCommandInfos()
            .Where(ci => ci.Group == "Macros")
            .OrderBy(ci => ci.Group);

        var groupMenu = new MenuEntry("Macros", string.Empty);

        foreach (var command in commands)
        {
            groupMenu.Children.Add(new MenuEntry(command.DisplayName, command.CommandName));
        }

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

    private async Task SelectImageButton_Click()
    {
        var result = await FileDialogHelper.OpenFileDialog();

        if (result == null || !File.Exists(result)) return;

        var image = SKBitmap.Decode(result);
        var scaledImage = BitmapHelper.ScaleAndPositionBitmap(
            image,
            90,
            90,
            ButtonData.ImageScale,
            ButtonData.ImagePositionX,
            ButtonData.ImagePositionY,
            BitmapHelper.ScalingOption.Fit);

        ButtonData.Image = scaledImage.ToRenderTargetBitmap();
    }

    private void RemoveImageButton_Click()
    {
        ButtonData.Image = null;
    }

    public void InsertCommand(MenuEntry menuEntry)
    {
        var formattedCommand = _commandBuilder.CreateCommandFromMenuEntry(menuEntry);

        ButtonData.Command += formattedCommand;
    }
}