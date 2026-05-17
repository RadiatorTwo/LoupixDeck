using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LoupixDeck.Views.Devices;

public partial class LoupedeckLiveSLayout : UserControl
{
    public LoupedeckLiveSLayout()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
