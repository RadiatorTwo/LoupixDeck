using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LoupixDeck.Models;
using LoupixDeck.ViewModels;

namespace LoupixDeck.Views.Devices;

public partial class RazerStreamControllerXLayout : UserControl
{
    public RazerStreamControllerXLayout()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    // The page-name text box binds its Name two-way (updated as you type), so a commit
    // only needs to persist the config. Enter commits and drops focus.
    private void OnPageNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        PageNameEditing.Save(sender);
        e.Handled = true;
    }

    private void OnPageNameCommit(object sender, RoutedEventArgs e) => PageNameEditing.Save(sender);

    // Single click selects a key, shown as a hover/selection frame; a double click opens
    // the editor. e.Handled stops the tap bubbling to OnBackgroundTapped (which clears it).
    // Every key is a TouchButton - this device has no dials, LEDs or side strips.
    private void OnButtonTapped(object sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm &&
            (sender as Button)?.CommandParameter is LoupedeckButton button)
        {
            vm.SelectButton(button);
            e.Handled = true;
        }
    }

    private void OnButtonDoubleTapped(object sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm &&
            (sender as Button)?.CommandParameter is TouchButton touch)
        {
            vm.TouchButtonCommand.Execute(touch);
        }
    }

    // Clicking empty device chrome clears the selection. Button taps set e.Handled, so they
    // never reach this bubbling handler.
    private void OnBackgroundTapped(object sender, TappedEventArgs e)
    {
        (DataContext as MainWindowViewModel)?.SelectButton(null);
    }

    // Right-click a key -> select it and open the Copy/Cut/Paste/Clear menu.
    private void OnButtonContextRequested(object sender, ContextRequestedEventArgs e)
        => LoupixDeck.Utils.DeviceButtonMenu.HandleContextRequested(e, DataContext as MainWindowViewModel);
}
