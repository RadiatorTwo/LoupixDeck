using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LoupixDeck.Models;
using LoupixDeck.ViewModels;

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

    // The page-name text boxes bind their Name two-way (updated as you type), so a
    // commit only needs to persist the config. Enter commits and drops focus.
    private void OnPageNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        PageNameEditing.Save(sender);
        e.Handled = true;
    }

    private void OnPageNameCommit(object sender, RoutedEventArgs e) => PageNameEditing.Save(sender);

    // Single click selects a button (touch tile, rotary dial, or LED), shown as a
    // hover/selection frame; a double click opens the matching editor. Every interactive
    // button carries its LoupedeckButton as CommandParameter, so selection is uniform.
    // e.Handled stops the tap bubbling to OnBackgroundTapped (which clears it).
    // (Live S has no side strips.)
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
        if (DataContext is not MainWindowViewModel vm ||
            (sender as Button)?.CommandParameter is not LoupedeckButton button)
            return;

        switch (button)
        {
            case TouchButton touch:
                vm.TouchButtonCommand.Execute(touch);
                break;
            case SimpleButton simple:
                vm.SimpleButtonCommand.Execute(simple);
                break;
            case RotaryButton rotary:
                vm.RotaryButtonCommand.Execute(rotary);
                break;
        }
    }

    // Clicking empty device chrome clears the selection. Button taps set e.Handled, so they
    // never reach this bubbling handler.
    private void OnBackgroundTapped(object sender, TappedEventArgs e)
    {
        (DataContext as MainWindowViewModel)?.SelectButton(null);
    }
}
