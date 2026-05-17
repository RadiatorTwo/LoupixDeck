using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LoupixDeck.Views.Devices;

public partial class RazerStreamControllerLayout : UserControl
{
    public RazerStreamControllerLayout()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
