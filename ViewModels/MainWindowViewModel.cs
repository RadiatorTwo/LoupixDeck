using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Controllers;
using LoupixDeck.Models;
using LoupixDeck.Services;
using LoupixDeck.ViewModels.Base;
using AsyncRelayCommand = CommunityToolkit.Mvvm.Input.AsyncRelayCommand;
using RelayCommand = LoupixDeck.Utils.RelayCommand;

namespace LoupixDeck.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly IDialogService _dialogService;

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

    public LoupedeckLiveSController LoupedeckController { get; }

    public MainWindowViewModel(LoupedeckLiveSController loupedeck,
        IDialogService dialogService,
        ISysCommandService sysCommandService)
    {
        LoupedeckController = loupedeck;

        sysCommandService.Initialize();

        _dialogService = dialogService;

        RotaryButtonCommand = new AsyncRelayCommand<RotaryButton>(RotaryButton_Click);
        SimpleButtonCommand = new AsyncRelayCommand<SimpleButton>(SimpleButton_Click);
        TouchButtonCommand = new AsyncRelayCommand<TouchButton>(TouchButton_Click);

        AddRotaryPageCommand = new RelayCommand(AddRotaryPageButton_Click);
        DeleteRotaryPageCommand = new RelayCommand(DeleteRotaryPageButton_Click);
        RotaryPageButtonCommand = new RelayCommand<int>(RotaryPageButton_Click);

        AddTouchPageCommand = new RelayCommand(AddTouchPageButton_Click);
        DeleteTouchPageCommand = new RelayCommand(DeleteTouchPageButton_Click);
        TouchPageButtonCommand = new RelayCommand<int>(TouchPageButton_Click);

        SettingsMenuCommand = new AsyncRelayCommand(SettingsMenuButton_Click);
        QuitApplicationCommand = new RelayCommand(QuitApplication);
    }

    private void AddRotaryPageButton_Click()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            LoupedeckController.PageManager.AddRotaryButtonPage();
            LoupedeckController.SaveConfig();
        });
    }

    private void DeleteRotaryPageButton_Click()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            LoupedeckController.PageManager.DeleteRotaryButtonPage();
            LoupedeckController.SaveConfig();
        });
    }

    private void RotaryPageButton_Click(int page)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            LoupedeckController.PageManager.ApplyRotaryPage(page - 1);
        });
    }

    private void AddTouchPageButton_Click()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            LoupedeckController.PageManager.AddTouchButtonPage();
            LoupedeckController.SaveConfig();
        });
    }

    private void DeleteTouchPageButton_Click()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            LoupedeckController.PageManager.DeleteTouchButtonPage();
            LoupedeckController.SaveConfig();
        });
    }

    private void TouchPageButton_Click(int page)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            LoupedeckController.PageManager.ApplyTouchPage(page - 1);
        });
    }

    private async Task RotaryButton_Click(RotaryButton button)
    {
        await _dialogService.ShowDialogAsync<RotaryButtonSettingsViewModel, DialogResult>(vm => vm.Initialize(button)
        );

        LoupedeckController.SaveConfig();
    }

    private async Task SimpleButton_Click(SimpleButton button)
    {
        await _dialogService.ShowDialogAsync<SimpleButtonSettingsViewModel, DialogResult>(vm => vm.Initialize(button)
        );

        LoupedeckController.SaveConfig();
    }

    private async Task TouchButton_Click(TouchButton button)
    {
        await _dialogService.ShowDialogAsync<TouchButtonSettingsViewModel, DialogResult>(vm => vm.Initialize(button)
        );

        LoupedeckController.SaveConfig();
    }

    private async Task SettingsMenuButton_Click()
    {
        await _dialogService.ShowDialogAsync<SettingsViewModel, DialogResult>();
        LoupedeckController.SaveConfig();
    }

    private void QuitApplication()
    {
        Environment.Exit(0);
    }
}