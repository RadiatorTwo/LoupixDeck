using System.Windows.Input;
using Avalonia.Media.Imaging;
using LoupixDeck.Models;
using LoupixDeck.Utils;

namespace LoupixDeck.ViewModels;

public class TouchButtonSettingsViewModel : ViewModelBase
{
    public ICommand SelectImageButtonCommand { get; }
    public ICommand RemoveImageButtonCommand { get; }
    public TouchButton ButtonData { get; set; }

    public TouchButtonSettingsViewModel(TouchButton buttonData)
    {
        ButtonData = buttonData;

        SelectImageButtonCommand = new AsyncRelayCommand(SelectImgageButton_Click);
        RemoveImageButtonCommand = new RelayCommand(RemoveImgageButton_Click);
    }

    private async Task SelectImgageButton_Click()
    {
        var parent = WindowHelper.GetMainWindow();
        if (parent == null) return;
        var result = await FileDialogHelper.OpenFileDialog(parent);

        if (result == null || !File.Exists(result.Path.AbsolutePath)) return;

        ButtonData.Image = new Bitmap(result.Path.AbsolutePath);
        ButtonData.RenderedImage = StaticDevice.Device.RenderTouchButtonContent(ButtonData, 150, 150);

        ButtonData.Refresh();
    }

    private void RemoveImgageButton_Click()
    {
        ButtonData.Image = null;

        ButtonData.RenderedImage = StaticDevice.Device.RenderTouchButtonContent(ButtonData, 150, 150);

        ButtonData.Refresh();
    }
}