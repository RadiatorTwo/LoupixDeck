using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LoupixDeck.Models;
using LoupixDeck.ViewModels;

namespace LoupixDeck.Views.Devices;

public partial class LoupedeckCtLayout : UserControl
{
    public LoupedeckCtLayout()
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

    // Single click selects a button (touch tile, side strip, rotary dial, or LED),
    // shown as a hover/selection frame; a double click opens the matching editor. Every
    // interactive button carries its LoupedeckButton as CommandParameter, so selection is
    // uniform. e.Handled stops the tap bubbling to OnBackgroundTapped (which clears it).
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

    // The side strips edit a per-side canvas rather than the displayed TouchButton, so they
    // keep a dedicated double-click handler that maps the button's Index -> RotarySide.
    private void OnStripDoubleTapped(object sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm &&
            (sender as Button)?.CommandParameter is TouchButton strip)
        {
            RotarySide side = strip.Index == LoupixDeck.LoupedeckDevice.Device.RazerStreamControllerDevice.RightSideIndex
                ? RotarySide.Right
                : RotarySide.Left;
            vm.EditStripCanvasCommand.Execute(side);
        }
    }

    // Clicking empty device chrome clears the selection. Button taps set e.Handled, so they
    // never reach this bubbling handler.
    private void OnBackgroundTapped(object sender, TappedEventArgs e)
    {
        (DataContext as MainWindowViewModel)?.SelectButton(null);
    }
}
