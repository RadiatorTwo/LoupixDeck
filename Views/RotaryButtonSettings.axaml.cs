using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LoupixDeck.Models;
using LoupixDeck.ViewModels;

namespace LoupixDeck.Views;

public partial class RotaryButtonSettings : Window
{
    public RotaryButtonSettings()
    {
        DataContext = new RotaryButtonSettingsViewModel(new RotaryButton());
        InitializeComponent();
    }
    
    public RotaryButtonSettings(RotaryButton buttonData)
    {
        DataContext = new RotaryButtonSettingsViewModel(buttonData);
        InitializeComponent();
    }
}