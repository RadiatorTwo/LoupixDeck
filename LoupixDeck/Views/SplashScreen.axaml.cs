using Avalonia.Controls;

namespace LoupixDeck.Views;

public partial class SplashScreen : Window
{
    public SplashScreen()
    {
        InitializeComponent();

        WindowDecorations = WindowDecorations.None;

        // Don know if we need this.
        //this.TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
    }
}