using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Models;
using LoupixDeck.Utils;
using LoupixDeck.Views;
using RelayCommand = LoupixDeck.Utils.RelayCommand;

namespace LoupixDeck.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    public ICommand RotaryButtonCommand { get; }
    public ICommand SimpleButtonCommand { get; }
    public ICommand TouchButtonCommand { get; }
    public ICommand AddPageCommand { get; }

    public LoupedeckLiveS LoupeDeckDevice { get; set; }

    public MainWindowViewModel()
    {
        LoupeDeckDevice = LoupedeckBase.LoadFromFile<LoupedeckLiveS>();

        if (LoupeDeckDevice == null)
        {
            LoupeDeckDevice = new LoupedeckLiveS();
        }

        if (LoupeDeckDevice.TouchButtonPages.Count == 0)
        {
            LoupeDeckDevice.AddPage();
        }
        else
        {
            LoupeDeckDevice.RefreshTouchButtons();
        }

        RotaryButtonCommand = new AsyncRelayCommand<RotaryButton>(RotaryButton_Click);
        SimpleButtonCommand = new AsyncRelayCommand<SimpleButton>(SimpleButton_Click);
        TouchButtonCommand = new AsyncRelayCommand<TouchButton>(TouchButton_Click);
        AddPageCommand = new RelayCommand(AddPageButton_Click);
    }

    private void AddPageButton_Click()
    {
        LoupeDeckDevice.AddPage();
    }

    private async Task RotaryButton_Click(RotaryButton button)
    {
        var newWindow = new RotaryButtonSettings(button)
        {
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner
        };
        await newWindow.ShowDialog(WindowHelper.GetMainWindow());

        LoupeDeckDevice.SaveToFile();
    }

    private async Task SimpleButton_Click(SimpleButton button)
    {
        var newWindow = new SimpleButtonSettings(button)
        {
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner
        };
        await newWindow.ShowDialog(WindowHelper.GetMainWindow());

        LoupeDeckDevice.SaveToFile();
    }

    private async Task TouchButton_Click(TouchButton button)
    {
        var newWindow = new TouchButtonSettings(button)
        {
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner
        };
        await newWindow.ShowDialog(WindowHelper.GetMainWindow());

        LoupeDeckDevice.SaveToFile();
    }
}