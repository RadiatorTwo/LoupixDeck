using Avalonia.Controls;
using LoupixDeck.ViewModels;

namespace LoupixDeck.Views;

public partial class InitSetup : Window
{
    public InitSetup()
    {
        InitializeComponent();
        
        Opened += (_, _) =>
        {
            if (DataContext is InitSetupViewModel vm)
            {
                vm.CloseWindow += () =>
                {
                    AllowClose();
                    Close();
                };
                
                vm.Init();
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

    public void AllowClose()
    {
        _allowClose = true;
    }
}