using Avalonia.Controls;
using Avalonia.Input;
using LoupixDeck.ViewModels;

namespace LoupixDeck.Views;

public partial class About : Window
{
    public About()
    {
        InitializeComponent();

        Opened += (_, _) =>
        {
            if (DataContext is AboutViewModel vm)
            {
                vm.CloseWindow += Close;
            }
        };

        // No Closing guard here: About has nothing to complete or confirm, so every close
        // path — the Close button, Alt+F4 — just closes it (issue #201). InitSetup keeps
        // its guard, that dialog must be filled in before the app can continue.
    }

    /// <summary>
    /// The window runs without system decorations, so it has no title bar to drag it by.
    /// A press on the chrome moves the window instead; presses that a control already
    /// handled (the buttons) never get here.
    /// </summary>
    private void OnChromePressed(object sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        BeginMoveDrag(e);
    }
}
