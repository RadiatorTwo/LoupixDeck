using Avalonia.Controls;
using Avalonia.Input.Platform;
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

    private KeyReferenceViewModel ViewModel => DataContext as KeyReferenceViewModel;

    private async void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var clipboard = GetTopLevel(this)?.Clipboard;
            if (clipboard != null && ViewModel != null)
                await clipboard.SetTextAsync(ViewModel.ToPlainText());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to copy the key list: {ex.Message}");
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
