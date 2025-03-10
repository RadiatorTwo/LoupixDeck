using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LoupixDeck.Models;
using LoupixDeck.ViewModels;

namespace LoupixDeck.Views;

public partial class SimpleButtonSettings : Window
{
    public SimpleButtonSettings()
    {
        DataContext = new SimpleButtonSettingsViewModel(new SimpleButton());
        InitializeComponent();
    }
    
    public SimpleButtonSettings(SimpleButton buttonData)
    {
        DataContext = new SimpleButtonSettingsViewModel(buttonData);
        InitializeComponent();
    }
}