using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LoupixDeck.Models;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels;

namespace LoupixDeck.Views.Devices;

public partial class LoupedeckCtLayout : UserControl
{
    public LoupedeckCtLayout()
    {
        InitializeComponent();
        DeviceButtonToolTip.Attach(this);
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

    // The wheel-mode label is bound two-way to the active mode's DisplayText; a commit persists
    // it and repaints the wheel screen, which draws that label.
    private void OnWheelLabelKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        CommitWheelLabel();
        e.Handled = true;
    }

    private void OnWheelLabelCommit(object sender, RoutedEventArgs e) => CommitWheelLabel();

    private void CommitWheelLabel()
    {
        if (DataContext is not MainWindowViewModel vm) return;
        vm.LoupedeckController.SaveConfig();
        _ = vm.LoupedeckController.RefreshWheel();
    }

    // "Edit appearance" edits the whole wheel screen, or, on a split wheel, the segment picked
    // from a menu listing the segments of the active mode.
    private void OnEditWheelAppearanceClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || sender is not Control anchor) return;

        RotaryButton wheel = vm.LoupedeckController.Config.CurrentTouchButtonPage?.Wheel;
        if (wheel is not { IsSplit: true })
        {
            vm.EditWheelCanvasCommand.Execute(null);
            return;
        }

        var menu = new MenuFlyout();
        int count = wheel.ActiveSegments().Count;
        for (int i = 0; i < count; i++)
        {
            menu.Items.Add(new MenuItem
            {
                Header = Localization.Loc.Tr(WheelSplitGeometry.SegmentNameKey(wheel.SplitMode, i)),
                Command = vm.EditWheelSegmentCanvasCommand,
                CommandParameter = i
            });
        }

        menu.ShowAt(anchor);
    }

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

    // Right-click a button → select it and open the Copy/Cut/Paste/Clear menu.
    private void OnButtonContextRequested(object sender, ContextRequestedEventArgs e)
        => LoupixDeck.Utils.DeviceButtonMenu.HandleContextRequested(e, DataContext as MainWindowViewModel);
}
