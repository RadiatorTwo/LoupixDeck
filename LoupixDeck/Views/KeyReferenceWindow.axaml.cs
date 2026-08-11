using Avalonia.Controls;
using Avalonia.Interactivity;
using LoupixDeck.ViewModels;

namespace LoupixDeck.Views;

/// <summary>
/// Non-modal reference listing the key names a key macro step accepts. Opened from the
/// macro editor next to the Capture button and owned by it, so it never blocks the editor
/// and never outlives it.
/// </summary>
public partial class KeyReferenceWindow : Window
{
    public KeyReferenceWindow()
    {
        DataContext = new KeyReferenceViewModel();
        InitializeComponent();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
