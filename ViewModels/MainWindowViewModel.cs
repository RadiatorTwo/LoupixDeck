﻿using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using LoupixDeck.Commands.Base;
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
    // private readonly ElgatoController _elgatoController;
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

    public LoupedeckLiveS LoupeDeckDevice { get; }

    public MainWindowViewModel(ObsController obs,
        ElgatoController elgatoController,
        ElgatoDevices elgatoDevices,
        DBusController dbus,
        CommandRunner runner)
    {
        _obs = obs;
        //_elgatoController = elgatoController;
        _elgatoDevices = elgatoDevices;

        LoupeDeckDevice = LoupedeckBase.LoadFromFile<LoupedeckLiveS>();
        
        if (LoupeDeckDevice == null)
        {
            LoupeDeckDevice = new LoupedeckLiveS();
            LoupeDeckDevice.InitDevice(true, obs, dbus, elgatoController, elgatoDevices, runner);
        }
        else
        {
            LoupeDeckDevice.InitDevice(false, obs, dbus, elgatoController, elgatoDevices, runner);
        }
        
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
    }

    public MainWindowViewModel()
    {
    }

    private void AddRotaryPageButton_Click()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { LoupeDeckDevice.AddRotaryButtonPage(); });
    }

    private void DeleteRotaryPageButton_Click()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { LoupeDeckDevice.DeleteRotaryButtonPage(); });
    }

    private void RotaryPageButton_Click(int page)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { LoupeDeckDevice.ApplyRotaryPage(page - 1); });
    }

    private void AddTouchPageButton_Click()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { LoupeDeckDevice.AddTouchButtonPage(); });
    }

    private void DeleteTouchPageButton_Click()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { LoupeDeckDevice.DeleteTouchButtonPage(); });
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
        var newWindow = new TouchButtonSettings(button, _obs, _elgatoDevices)
        {
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner
        };
        await newWindow.ShowDialog(WindowHelper.GetMainWindow());

        LoupeDeckDevice.SaveToFile();
    }

    private async Task SettingsMenuButton_Click()
    {
        var newWindow = new Settings(_obs)
        {
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner
        };
        await newWindow.ShowDialog(WindowHelper.GetMainWindow());
    }
}