using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LoupixDeck.Views;

public partial class SplashScreen : Window
{
    public SplashScreen()
    {
        InitializeComponent();
        
        SystemDecorations = SystemDecorations.None;

        // Don know if we need this.
        //this.TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
    }
}