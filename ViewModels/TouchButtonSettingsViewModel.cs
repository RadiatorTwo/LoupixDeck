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
    public ICommand SelectImageButtonCommand { get; }
    public ICommand RemoveImageButtonCommand { get; }
    public TouchButton ButtonData { get; }
    
    public ObservableCollection<SystemCommand> SystemCommandMenus { get; set; }
    public SystemCommand CurrentSystemCommand { get; set; }

    public TouchButtonSettingsViewModel(TouchButton buttonData, ObsController obs)
    {
        _obs = obs;
        ButtonData = buttonData;

        SelectImageButtonCommand = new AsyncRelayCommand(SelectImgageButton_Click);
        RemoveImageButtonCommand = new RelayCommand(RemoveImgageButton_Click);

        SystemCommandMenus =  new ObservableCollection<SystemCommand>();
        
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

    public void InsertCommand(Constants.SystemCommand command, params object[] replacements)
    {
        var systemCommand = Constants.SystemCommands.Reverse[command];

        var formattedCommand = string.Format(systemCommand, replacements);

        ButtonData.Command += formattedCommand;
    }
}