using Avalonia.Controls;
using LoupixDeck.Models;
using LoupixDeck.ViewModels;

namespace LoupixDeck.Views;

public partial class TouchButtonSettings : Window
{
    public TouchButtonSettings()
    {
        DataContext = new TouchButtonSettingsViewModel(new TouchButton(-1));
        InitializeComponent();
    }
    
    public TouchButtonSettings(TouchButton buttonData)
    {
        DataContext = new TouchButtonSettingsViewModel(buttonData);
        InitializeComponent();
    }
}