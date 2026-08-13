using Avalonia.Controls;
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

        // No Closing guard here: About has nothing to complete or confirm, so the title
        // bar's X closes it like every other window (issue #201). InitSetup keeps its
        // guard — that dialog must be filled in before the app can continue.
    }
}
