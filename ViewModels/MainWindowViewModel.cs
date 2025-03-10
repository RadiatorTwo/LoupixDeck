using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Media;
using LoupixDeck.LoupedeckDevice;
using LoupixDeck.LoupedeckDevice.Device;
using LoupixDeck.Models;
using LoupixDeck.Utils;
using LoupixDeck.Views;

namespace LoupixDeck.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    public ICommand RotaryButtonCommand { get; }
    public ICommand SimpleButtonCommand { get; }
    public ICommand TouchButtonCommand { get; }

    public LoupedeckLiveS LoupeDeckDevice { get; set; }

    public MainWindowViewModel()
    {
        LoupeDeckDevice = new LoupedeckLiveS();
        
        RotaryButtonCommand = new RelayCommand<RotaryButton>(RotaryButton_Click);
        SimpleButtonCommand = new RelayCommand<SimpleButton>(SimpleButton_Click);
        TouchButtonCommand = new RelayCommand<TouchButton>(TouchButton_Click);
    }

    private void RotaryButton_Click(RotaryButton button)
    {
        var newWindow = new RotaryButtonSettings(button)
        {
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner
        };
        newWindow.ShowDialog(WindowHelper.GetMainWindow());
    }

    private void SimpleButton_Click(SimpleButton button)
    {
        var newWindow = new SimpleButtonSettings(button)
        {
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner
        };
        newWindow.ShowDialog(WindowHelper.GetMainWindow());
    }

    private void TouchButton_Click(TouchButton button)
    {
        var newWindow = new TouchButtonSettings(button)
        {
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner
        };
        newWindow.ShowDialog(WindowHelper.GetMainWindow());
    }
}