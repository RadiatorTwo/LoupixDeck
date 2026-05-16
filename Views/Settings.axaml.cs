using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LoupixDeck.Models;
using LoupixDeck.Services;
using LoupixDeck.ViewModels;
using LoupixDeck.ViewModels.Base;

namespace LoupixDeck.Views;

public partial class Settings : Window
{
    public Settings() : this(null) { }

    public Settings(SettingsViewModel vm)
    {
        // Set DataContext before XAML load so $parent[Window].DataContext bindings
        // in DataTemplates have a non-null target on first evaluation.
        if (vm != null)
            DataContext = vm;

        InitializeComponent();

        Closing += (_, _) =>
        {
            if (DataContext is IDialogViewModel dlg && !dlg.DialogResult.Task.IsCompleted)
            {
                dlg.DialogResult.TrySetResult(new DialogResult(false));
            }
        };
    }
}