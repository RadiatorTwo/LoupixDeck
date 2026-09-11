using Avalonia.Controls;
using LoupixDeck.ViewModels;

namespace LoupixDeck.Views;

/// <summary>Names a dial preset. Purely markup; everything happens in the view model.</summary>
public partial class DialPresetEditor : Window
{
    public DialPresetEditor()
    {
        InitializeComponent();
    }

    public DialPresetEditor(DialPresetEditorViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
