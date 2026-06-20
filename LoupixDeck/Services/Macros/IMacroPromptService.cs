using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LoupixDeck.Utils;

namespace LoupixDeck.Services.Macros;

/// <summary>Shows a runtime text-input prompt for a macro Prompt step.</summary>
public interface IMacroPromptService
{
    /// <summary>
    /// Shows a modal text-input prompt and returns the entered text, or null if the user
    /// cancelled or the run was stopped. Safe to call from the runner's background thread —
    /// it marshals to the UI thread internally. The prompt closes automatically when
    /// <paramref name="token"/> is cancelled (the global Stop).
    /// </summary>
    Task<string> RequestInputAsync(string message, string defaultValue, CancellationToken token);
}

/// <inheritdoc cref="IMacroPromptService"/>
public sealed class MacroPromptService : IMacroPromptService
{
    public async Task<string> RequestInputAsync(string message, string defaultValue, CancellationToken token)
    {
        if (token.IsCancellationRequested)
            return null;

        // Hop to the UI thread; ShowPrompt awaits the modal dialog there.
        return await Dispatcher.UIThread.InvokeAsync(() => ShowPrompt(message, defaultValue, token));
    }

    private static async Task<string> ShowPrompt(string message, string defaultValue, CancellationToken token)
    {
        var owner = WindowHelper.GetMainWindow();
        if (owner == null)
            return null;

        var tcs = new TaskCompletionSource<string>();

        var window = new Window
        {
            Title = "Macro Input",
            Width = 420,
            Height = 190,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowDecorations = WindowDecorations.Full
        };

        var messageBlock = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(message) ? "Enter a value:" : message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(20, 18, 20, 8)
        };

        var inputBox = new TextBox
        {
            Text = defaultValue ?? string.Empty,
            Margin = new Thickness(20, 0, 20, 0)
        };

        var okButton = new Button
        {
            Content = "OK",
            Width = 90,
            Margin = new Thickness(0, 0, 10, 0),
            IsDefault = true,
            Background = new SolidColorBrush(Color.FromRgb(0xA0, 0x30, 0x30)),
            Foreground = Brushes.White
        };
        okButton.Click += (_, _) => { tcs.TrySetResult(inputBox.Text ?? string.Empty); window.Close(); };

        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 90,
            IsCancel = true
        };
        cancelButton.Click += (_, _) => { tcs.TrySetResult(null); window.Close(); };

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 20, 15),
            Children = { okButton, cancelButton }
        };

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            Children = { messageBlock, inputBox, buttonRow }
        };
        Grid.SetRow(messageBlock, 0);
        Grid.SetRow(inputBox, 1);
        Grid.SetRow(buttonRow, 3);

        window.Content = grid;
        // A bare close (window X) counts as cancel; TrySet is a no-op if OK already resolved.
        window.Closing += (_, _) => tcs.TrySetResult(null);
        inputBox.AttachedToVisualTree += (_, _) => { inputBox.SelectAll(); inputBox.Focus(); };

        // Stop / cancel closes the prompt and unblocks the parked macro.
        await using var registration = token.Register(() =>
            Dispatcher.UIThread.Post(() => { tcs.TrySetResult(null); window.Close(); }));

        await window.ShowDialog(owner);
        return await tcs.Task;
    }
}
