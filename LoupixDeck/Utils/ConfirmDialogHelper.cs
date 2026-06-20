using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace LoupixDeck.Utils;

/// <summary>
/// Tiny modal Yes/No confirmation. Built in code so no extra XAML / DI plumbing is needed.
/// </summary>
public static class ConfirmDialogHelper
{
    public static async Task<bool> AskYesNoAsync(Window owner, string title, string message)
    {
        var tcs = new TaskCompletionSource<bool>();

        var window = new Window
        {
            Title = title,
            Width = 380,
            Height = 170,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowDecorations = WindowDecorations.Full
        };

        var messageBlock = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(20),
            VerticalAlignment = VerticalAlignment.Top
        };

        var yesButton = new Button
        {
            Content = "Yes",
            Width = 90,
            Margin = new Thickness(0, 0, 10, 0),
            Background = new SolidColorBrush(Color.FromRgb(0xA0, 0x30, 0x30)),
            Foreground = Brushes.White
        };
        yesButton.Click += (_, _) => { tcs.TrySetResult(true); window.Close(); };

        var noButton = new Button
        {
            Content = "No",
            Width = 90,
            IsDefault = true
        };
        noButton.Click += (_, _) => { tcs.TrySetResult(false); window.Close(); };

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 20, 15),
            Children = { yesButton, noButton }
        };

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Children = { messageBlock, buttonRow }
        };
        Grid.SetRow(messageBlock, 0);
        Grid.SetRow(buttonRow, 1);

        window.Content = grid;
        window.Closing += (_, _) => tcs.TrySetResult(false);

        await window.ShowDialog(owner);
        return await tcs.Task;
    }
}
