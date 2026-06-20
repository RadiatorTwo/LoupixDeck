using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
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
                vm.CloseWindow += () =>
                {
                    AllowClose();
                    Close();
                };
                
                // vm.Init();
            }
        };

        Closing += OnWindowClosing;
    }
    
    private bool _allowClose;

    private void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
        }
    }

    private void AllowClose()
    {
        _allowClose = true;
    }
}