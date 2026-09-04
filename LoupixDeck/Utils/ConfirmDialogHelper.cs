using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace LoupixDeck.Utils;

/// <summary>
/// Tiny modal confirmations. Built in code so no extra XAML / DI plumbing is needed.
/// </summary>
public static class ConfirmDialogHelper
{
    /// <summary>
    /// Two-way choice between keeping and discarding something, with no cancel: closing the
    /// window keeps (the non-destructive option), exactly like <see cref="AskYesNoAsync"/>
    /// defaults to "No". Returns true when the user chose to keep.
    /// </summary>
    public static async Task<bool> AskKeepDiscardAsync(Window owner, string title, string message,
        string keepLabel, string discardLabel)
    {
        var tcs = new TaskCompletionSource<bool>();

        var window = new Window
        {
            Title = title,
            Width = 420,
            Height = 190,
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

        var discardButton = new Button
        {
            Content = discardLabel,
            MinWidth = 110,
            Margin = new Thickness(0, 0, 10, 0),
            Background = new SolidColorBrush(Color.FromRgb(0xA0, 0x30, 0x30)),
            Foreground = Brushes.White
        };
        discardButton.Click += (_, _) => { tcs.TrySetResult(false); window.Close(); };

        var keepButton = new Button
        {
            Content = keepLabel,
            MinWidth = 110,
            IsDefault = true
        };
        keepButton.Click += (_, _) => { tcs.TrySetResult(true); window.Close(); };

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 20, 15),
            Children = { discardButton, keepButton }
        };

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Children = { messageBlock, buttonRow }
        };
        Grid.SetRow(messageBlock, 0);
        Grid.SetRow(buttonRow, 1);

        window.Content = grid;
        window.Closing += (_, _) => tcs.TrySetResult(true);

        await window.ShowDialog(owner);
        return await tcs.Task;
    }

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
