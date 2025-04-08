using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Models;
using LoupixDeck.Services;
using LoupixDeck.Utils;
using LoupixDeck.Views;
using AsyncRelayCommand = CommunityToolkit.Mvvm.Input.AsyncRelayCommand;
using RelayCommand = LoupixDeck.Utils.RelayCommand;

namespace LoupixDeck.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly ObsController _obs;
    private readonly ElgatoDevices _elgatoDevices;
    public ICommand RotaryButtonCommand { get; }
    public ICommand SimpleButtonCommand { get; }
    public ICommand TouchButtonCommand { get; }

    public ICommand AddRotaryPageCommand { get; }
    public ICommand DeleteRotaryPageCommand { get; }
    public ICommand RotaryPageButtonCommand { get; }


    public ICommand AddTouchPageCommand { get; }
    public ICommand DeleteTouchPageCommand { get; }
    public ICommand TouchPageButtonCommand { get; }

    public ICommand SettingsMenuCommand { get; }
    public ICommand QuitApplicationCommand { get; }

    public LoupedeckLiveS LoupeDeck { get; }

    public MainWindowViewModel(LoupedeckLiveS loupedeck,
                               ObsController obs,
                               ElgatoDevices elgatoDevices)
    {
        LoupeDeck = loupedeck;
        LoupeDeck.InitDevice();

        _obs = obs;
        _elgatoDevices = elgatoDevices;

        RotaryButtonCommand = new AsyncRelayCommand<RotaryButton>(RotaryButton_Click);
        SimpleButtonCommand = new AsyncRelayCommand<SimpleButton>(SimpleButton_Click);
        TouchButtonCommand = new AsyncRelayCommand<TouchButton>(TouchButton_Click);

        AddRotaryPageCommand = new RelayCommand(AddRotaryPageButton_Click);
        DeleteRotaryPageCommand = new RelayCommand(DeleteRotaryPageButton_Click);
        RotaryPageButtonCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<int>(RotaryPageButton_Click);

        AddTouchPageCommand = new RelayCommand(AddTouchPageButton_Click);
        DeleteTouchPageCommand = new RelayCommand(DeleteTouchPageButton_Click);
        TouchPageButtonCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<int>(TouchPageButton_Click);

        SettingsMenuCommand = new AsyncRelayCommand(SettingsMenuButton_Click);
        QuitApplicationCommand = new RelayCommand(QuitApplication);
    }

    public MainWindowViewModel()
    {
    }

    private void AddRotaryPageButton_Click()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { LoupeDeck.AddRotaryButtonPage(); });
    }

    private void DeleteRotaryPageButton_Click()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { LoupeDeck.DeleteRotaryButtonPage(); });
    }

    private void RotaryPageButton_Click(int page)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { LoupeDeck.ApplyRotaryPage(page - 1); });
    }

    private void AddTouchPageButton_Click()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { LoupeDeck.AddTouchButtonPage(); });
    }

    private void DeleteTouchPageButton_Click()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { LoupeDeck.DeleteTouchButtonPage(); });
    }

    private void TouchPageButton_Click(int page)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { LoupeDeck.ApplyTouchPage(page - 1); });
    }

    private async Task RotaryButton_Click(RotaryButton button)
    {
        var newWindow = new RotaryButtonSettings(button, _obs, _elgatoDevices)
        {
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner
        };
        await newWindow.ShowDialog(WindowHelper.GetMainWindow());

        LoupeDeck.SaveToFile();
    }

    private async Task SimpleButton_Click(SimpleButton button)
    {
        var newWindow = new SimpleButtonSettings(button, _obs, _elgatoDevices)
        {
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner
        };
        await newWindow.ShowDialog(WindowHelper.GetMainWindow());

        LoupeDeck.SaveToFile();
    }

    private async Task TouchButton_Click(TouchButton button)
    {
        var newWindow = new TouchButtonSettings(button, _obs, _elgatoDevices)
        {
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner
        };
        await newWindow.ShowDialog(WindowHelper.GetMainWindow());

        LoupeDeck.SaveToFile();
    }

    private async Task SettingsMenuButton_Click()
    {
        var newWindow = new Settings(_obs)
        {
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner
        };
        await newWindow.ShowDialog(WindowHelper.GetMainWindow());
    }

    private void QuitApplication()
    {
        Environment.Exit(0);
    }
}