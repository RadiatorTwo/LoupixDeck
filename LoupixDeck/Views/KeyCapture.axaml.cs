using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LoupixDeck.Models;
using LoupixDeck.ViewModels;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.Views;

public partial class KeyCapture : Window
{
    public KeyCapture()
    {
        InitializeComponent();

        // Tunnel + handledEventsToo so every key reaches the view model before any control can
        // act on it — the dialog is recording keys, so Tab, Space and accelerators must not do
        // their usual thing while it is open. Escape stays free so the window can be dismissed.
        AddHandler(KeyDownEvent, OnCaptureKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyUpEvent, OnCaptureKeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);

        Opened += (_, _) =>
        {
            if (DataContext is KeyCaptureViewModel vm)
                vm.CloseRequested += Close;

            Focus();
        };

        Closing += (_, _) =>
        {
            // Closing via the window chrome (X) counts as a cancel.
            if (DataContext is IDialogViewModel vm && !vm.DialogResult.Task.IsCompleted)
                vm.DialogResult.TrySetResult(new DialogResult(false));
        };
    }

    private void OnCaptureKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            return;

        if (DataContext is KeyCaptureViewModel vm)
            vm.HandleKeyDown(e.Key);

        e.Handled = true;
    }

    private void OnCaptureKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            return;

        if (DataContext is KeyCaptureViewModel vm)
            vm.HandleKeyUp(e.Key);

        e.Handled = true;
    }
}
