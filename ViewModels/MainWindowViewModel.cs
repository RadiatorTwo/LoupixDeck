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
    public ICommand AddRotaryPageCommand { get; }
    public ICommand AddTouchPageCommand { get; }
    public ICommand RotaryPageButtonCommand { get; }
    public ICommand TouchPageButtonCommand { get; }

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
            LoupeDeckDevice.AddTouchButtonPage();
        }
        else
        {
            LoupeDeckDevice.RefreshTouchButtons();
        }

        if (LoupeDeckDevice.RotaryButtonPages.Count == 0)
        {
            LoupeDeckDevice.AddRotaryButtonPage();
        }

        RotaryButtonCommand = new AsyncRelayCommand<RotaryButton>(RotaryButton_Click);
        SimpleButtonCommand = new AsyncRelayCommand<SimpleButton>(SimpleButton_Click);
        TouchButtonCommand = new AsyncRelayCommand<TouchButton>(TouchButton_Click);
        AddRotaryPageCommand = new RelayCommand(AddRotaryPageButton_Click);
        AddTouchPageCommand = new RelayCommand(AddTouchPageButton_Click);
        RotaryPageButtonCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<int>(RotaryPageButton_Click);
        TouchPageButtonCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<int>(TouchPageButton_Click);
    }

    private void AddRotaryPageButton_Click()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { LoupeDeckDevice.AddRotaryButtonPage(); });
    }

    private void AddTouchPageButton_Click()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { LoupeDeckDevice.AddTouchButtonPage(); });
    }

    private void RotaryPageButton_Click(int page)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { LoupeDeckDevice.ApplyRotaryPage(page - 1); });
    }

    private void TouchPageButton_Click(int page)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { LoupeDeckDevice.ApplyTouchPage(page - 1); });
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