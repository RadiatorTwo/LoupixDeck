using System.Collections.ObjectModel;
using LoupixDeck.Models;

namespace LoupixDeck.ViewModels;

/// <summary>
/// Editor row wrapper for a single <see cref="AppPageBinding"/>. Exposes the page
/// option lists as its own properties so the row's ComboBoxes bind their
/// <c>ItemsSource</c> against the immediate DataContext (resolved synchronously),
/// instead of an ancestor lookup that resolves after <c>SelectedIndex</c> and would
/// momentarily leave the list empty — which makes Avalonia coerce the selection to
/// -1 and write that back over the user's choice.
/// </summary>
public sealed class AppBindingRow
{
    public AppPageBinding Binding { get; }
    public ObservableCollection<TouchButtonPage> TouchPages { get; }
    public ObservableCollection<string> RotaryPageOptions { get; }

    public AppBindingRow(AppPageBinding binding,
        ObservableCollection<TouchButtonPage> touchPages,
        ObservableCollection<string> rotaryPageOptions)
    {
        Binding = binding;
        TouchPages = touchPages;
        RotaryPageOptions = rotaryPageOptions;
    }
}
