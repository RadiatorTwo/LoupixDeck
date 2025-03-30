using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using LoupixDeck.LoupedeckDevice;
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

    public ObservableCollection<SystemCommand> SystemCommandMenus { get; set; }
    public SystemCommand CurrentSystemCommand { get; set; }

    private SystemCommand _elgatoKeyLightMenu;

    public TouchButtonSettingsViewModel(TouchButton buttonData,
        ObsController obs,
        ElgatoDevices elgatoDevices)
    {
        _obs = obs;
        _elgatoDevices = elgatoDevices;
        ButtonData = buttonData;

        SelectImageButtonCommand = new AsyncRelayCommand(SelectImgageButton_Click);
        RemoveImageButtonCommand = new RelayCommand(RemoveImgageButton_Click);

        SystemCommandMenus = new ObservableCollection<SystemCommand>();

        CreateSystemMenu();
    }

    private void CreateSystemMenu()
    {
        // Pages
        var pageMenu = new SystemCommand("Pages", Constants.SystemCommand.NONE);

        pageMenu.Childs.Add(new SystemCommand("Next Page", Constants.SystemCommand.NEXT_PAGE));
        pageMenu.Childs.Add(new SystemCommand("Previous Page", Constants.SystemCommand.PREVIOUS_PAGE));
        pageMenu.Childs.Add(new SystemCommand("Next Rotary Page", Constants.SystemCommand.NEXT_ROT_PAGE));
        pageMenu.Childs.Add(new SystemCommand("Previous Rotary Page", Constants.SystemCommand.PREVIOUS_ROT_PAGE));

        SystemCommandMenus.Add(pageMenu);

        // OBS Menu
        var obsMenu = new SystemCommand("OBS", Constants.SystemCommand.NONE);

        obsMenu.Childs.Add(new SystemCommand("Start Record", Constants.SystemCommand.OBS_START_RECORD));
        obsMenu.Childs.Add(new SystemCommand("Stop Record", Constants.SystemCommand.OBS_STOP_RECORD));
        obsMenu.Childs.Add(new SystemCommand("Pause Record", Constants.SystemCommand.OBS_PAUSE_RECORD));

        obsMenu.Childs.Add(new SystemCommand("Start Replay", Constants.SystemCommand.OBS_START_REPLAY));
        obsMenu.Childs.Add(new SystemCommand("Stop Replay", Constants.SystemCommand.OBS_STOP_REPLAY));
        obsMenu.Childs.Add(new SystemCommand("Save Replay", Constants.SystemCommand.OBS_SAVE_REPLAY));

        obsMenu.Childs.Add(new SystemCommand("Toggle Virtual Camera", Constants.SystemCommand.OBS_VIRTUAL_CAM));

        var scenesMenu = new SystemCommand("Scenes", Constants.SystemCommand.NONE);
        var scenes = _obs.GetScenes();

        foreach (var scene in scenes)
        {
            scenesMenu.Childs.Add(new SystemCommand(scene.Name, Constants.SystemCommand.OBS_SET_SCENE));
        }

        obsMenu.Childs.Add(scenesMenu);

        SystemCommandMenus.Add(obsMenu);

        // Elgato Menu
        var elgatoMenu = new SystemCommand("Elgato", Constants.SystemCommand.NONE);
        _elgatoKeyLightMenu = new SystemCommand("Keylights", Constants.SystemCommand.NONE);

        elgatoMenu.Childs.Add(_elgatoKeyLightMenu);

        SystemCommandMenus.Add(elgatoMenu);

        foreach (var keyLight in _elgatoDevices.KeyLights)
        {
            AddKeyLightMenuEntry(keyLight);
        }

        _elgatoDevices.KeyLightAdded += KeyLightAdded;
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

        var newKeyLightCommand = new SystemCommand(keyLight.DisplayName, Constants.SystemCommand.NONE);

        var newToggle = new SystemCommand("Turn On/Off", Constants.SystemCommand.ELG_KL_TOGGLE,
            keyLight.DisplayName);
        newKeyLightCommand.Childs.Add(newToggle);

        if (keyLight.Brightness > 0)
        {
            var newBrightness = new SystemCommand("Brightness", Constants.SystemCommand.ELG_KL_BRIGHTNESS,
                keyLight.DisplayName);
            newKeyLightCommand.Childs.Add(newBrightness);
        }

        if (keyLight.Temperature > 0)
        {
            var newTemperature = new SystemCommand("Temperature", Constants.SystemCommand.ELG_KL_TEMPERATURE,
                keyLight.DisplayName);
            newKeyLightCommand.Childs.Add(newTemperature);
        }

        if (keyLight.Hue > 0)
        {
            var newHue = new SystemCommand("Hue", Constants.SystemCommand.ELG_KL_HUE, keyLight.DisplayName);
            newKeyLightCommand.Childs.Add(newHue);
        }

        if (keyLight.Saturation > 0)
        {
            var newSaturation = new SystemCommand("Saturation", Constants.SystemCommand.ELG_KL_SATURATION,
                keyLight.DisplayName);
            newKeyLightCommand.Childs.Add(newSaturation);
        }

        _elgatoKeyLightMenu.Childs.Add(newKeyLightCommand);
    }

    private async Task SelectImgageButton_Click()
    {
        var parent = WindowHelper.GetMainWindow();
        if (parent == null) return;
        var result = await FileDialogHelper.OpenFileDialog(parent);

        if (result == null || !File.Exists(result.Path.AbsolutePath)) return;

        ButtonData.Image = new Bitmap(result.Path.AbsolutePath);
        ButtonData.RenderedImage = BitmapHelper.RenderTouchButtonContent(ButtonData, 150, 150);

        ButtonData.Refresh();
    }

    private void RemoveImgageButton_Click()
    {
        ButtonData.Image = null;

        ButtonData.RenderedImage = BitmapHelper.RenderTouchButtonContent(ButtonData, 150, 150);

        ButtonData.Refresh();
    }

    public void InsertCommand(SystemCommand command)
    {
        var systemCommand =
            Constants.SystemCommands.Reverse.FirstOrDefault(
                x => x.Key.SystemCommand == command.Command);

        var formattedCommand = systemCommand.Value;

        if (systemCommand.Key.Parametered)
        {
            formattedCommand += "(";

            switch (systemCommand.Key.SystemCommand)
            {
                case Constants.SystemCommand.OBS_SET_SCENE:
                    formattedCommand += command.Name;
                    break;

                case Constants.SystemCommand.ELG_KL_TOGGLE:
                    formattedCommand += command.ParentName;
                    break;

                case Constants.SystemCommand.ELG_KL_TEMPERATURE:
                case Constants.SystemCommand.ELG_KL_BRIGHTNESS:
                case Constants.SystemCommand.ELG_KL_SATURATION:
                case Constants.SystemCommand.ELG_KL_HUE:
                    formattedCommand += command.ParentName;
                    formattedCommand += ",value";
                    break;
            }

            formattedCommand += ")";
        }

        ButtonData.Command += formattedCommand;
    }
}