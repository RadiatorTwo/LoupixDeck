using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LoupixDeck.Models.Macros;
using LoupixDeck.Utils;

namespace LoupixDeck.Services.Macros;

/// <summary>
/// A fully-resolved request for a runtime macro prompt. Built from a <see cref="PromptStep"/> by the
/// runner, which expands <c>{variable}</c> placeholders in <see cref="Message"/>/<see cref="DefaultValue"/>
/// first. Carries the input type and restrictions so the dialog can validate before returning.
/// </summary>
public sealed record MacroPromptRequest
{
    public string Message { get; init; } = string.Empty;
    public string DefaultValue { get; init; } = string.Empty;
    public PromptInputType InputType { get; init; } = PromptInputType.Text;
    public bool AllowEmpty { get; init; } = true;
    public double? Minimum { get; init; }
    public double? Maximum { get; init; }
    public bool AllowZero { get; init; } = true;
    public bool AllowNegative { get; init; } = true;
    public int? MinLength { get; init; }
    public int? MaxLength { get; init; }
    public string ValidationRegex { get; init; } = string.Empty;
    public IReadOnlyList<string> SelectionItems { get; init; } = [];

    /// <summary>
    /// Builds a request from a step, expanding placeholders in the message, default value, and
    /// selection items via <paramref name="expand"/>.
    /// </summary>
    public static MacroPromptRequest FromStep(PromptStep step, Func<string, string> expand) => new()
    {
        Message = expand(step.Message),
        DefaultValue = expand(step.DefaultValue),
        InputType = step.InputType,
        AllowEmpty = step.AllowEmpty,
        Minimum = step.Minimum,
        Maximum = step.Maximum,
        AllowZero = step.AllowZero,
        AllowNegative = step.AllowNegative,
        MinLength = step.MinLength,
        MaxLength = step.MaxLength,
        ValidationRegex = step.ValidationRegex,
        SelectionItems = step.SelectionItems.Select(expand).ToList()
    };
}

/// <summary>Shows a runtime input prompt for a macro Prompt step.</summary>
public interface IMacroPromptService
{
    /// <summary>
    /// Shows a modal input prompt and returns the validated, normalised value, or null if the user
    /// cancelled or the run was stopped. Safe to call from the runner's background thread — it
    /// marshals to the UI thread internally. The prompt closes automatically when
    /// <paramref name="token"/> is cancelled (the global Stop).
    /// </summary>
    Task<string> RequestInputAsync(MacroPromptRequest request, CancellationToken token);
}

/// <inheritdoc cref="IMacroPromptService"/>
public sealed class MacroPromptService : IMacroPromptService
{
    public async Task<string> RequestInputAsync(MacroPromptRequest request, CancellationToken token)
    {
        if (token.IsCancellationRequested)
            return null;

        // Hop to the UI thread; ShowPrompt awaits the modal dialog there.
        return await Dispatcher.UIThread.InvokeAsync(() => ShowPrompt(request, token));
    }

    private static async Task<string> ShowPrompt(MacroPromptRequest request, CancellationToken token)
    {
        var owner = WindowHelper.GetMainWindow();
        if (owner == null)
            return null;

        var tcs = new TaskCompletionSource<string>();

        var window = new Window
        {
            Title = "Macro Input",
            Width = 420,
            Height = 220,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowDecorations = WindowDecorations.Full
        };

        var messageBlock = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(request.Message) ? "Enter a value:" : request.Message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(20, 18, 20, 8)
        };

        // The input control varies by type; getRawInput reads its current value for validation.
        Control inputControl = BuildInputControl(request, out Func<string> getRawInput, out Action focusInput);

        var errorBlock = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0x60, 0x60)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(20, 4, 20, 0),
            IsVisible = false
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
        okButton.Click += (_, _) =>
        {
            if (PromptValidation.TryValidate(request, getRawInput(), out string normalized, out string error))
            {
                tcs.TrySetResult(normalized);
                window.Close();
            }
            else
            {
                errorBlock.Text = error;
                errorBlock.IsVisible = true;
            }
        };

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
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"),
            Children = { messageBlock, inputControl, errorBlock, buttonRow }
        };
        Grid.SetRow(messageBlock, 0);
        Grid.SetRow(inputControl, 1);
        Grid.SetRow(errorBlock, 2);
        Grid.SetRow(buttonRow, 4);

        window.Content = grid;
        // A bare close (window X) counts as cancel; TrySet is a no-op if OK already resolved.
        window.Closing += (_, _) => tcs.TrySetResult(null);
        inputControl.AttachedToVisualTree += (_, _) => focusInput();

        // Stop / cancel closes the prompt and unblocks the parked macro.
        await using var registration = token.Register(() =>
            Dispatcher.UIThread.Post(() => { tcs.TrySetResult(null); window.Close(); }));

        await window.ShowDialog(owner);
        return await tcs.Task;
    }

    /// <summary>Creates the type-specific input control and the accessors the dialog needs.</summary>
    private static Control BuildInputControl(MacroPromptRequest request, out Func<string> getRawInput,
        out Action focusInput)
    {
        var margin = new Thickness(20, 0, 20, 0);

        switch (request.InputType)
        {
            case PromptInputType.Boolean:
                {
                    var combo = new ComboBox
                    {
                        Margin = margin,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        ItemsSource = new[] { "Yes", "No" },
                        SelectedItem = IsTruthy(request.DefaultValue) ? "Yes" : "No"
                    };
                    getRawInput = () => combo.SelectedItem as string ?? "No";
                    focusInput = () => combo.Focus();
                    return combo;
                }

            case PromptInputType.Selection:
                {
                    var combo = new ComboBox
                    {
                        Margin = margin,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        ItemsSource = request.SelectionItems
                    };
                    if (!string.IsNullOrEmpty(request.DefaultValue) &&
                        request.SelectionItems.Contains(request.DefaultValue))
                        combo.SelectedItem = request.DefaultValue;
                    getRawInput = () => combo.SelectedItem as string ?? string.Empty;
                    focusInput = () => combo.Focus();
                    return combo;
                }

            default:
                {
                    var textBox = new TextBox
                    {
                        Text = request.DefaultValue ?? string.Empty,
                        Margin = margin
                    };
                    getRawInput = () => textBox.Text ?? string.Empty;
                    focusInput = () => { textBox.SelectAll(); textBox.Focus(); };
                    return textBox;
                }
        }
    }

    private static bool IsTruthy(string value) =>
        value?.Trim().ToLowerInvariant() is "true" or "yes" or "1" or "on";
}