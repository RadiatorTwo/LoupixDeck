using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LoupixDeck.Services;
using LoupixDeck.ViewModels;

namespace LoupixDeck.Views;

public partial class Settings : Window
{
    public Settings()
    {
        DataContext = new SettingsViewModel(new ObsController());
        InitializeComponent();
    }
    
    public Settings(ObsController obs)
    {
        DataContext = new SettingsViewModel(obs);
        InitializeComponent();
    }
}