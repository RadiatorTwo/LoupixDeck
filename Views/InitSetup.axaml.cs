using Avalonia.Controls;
using LoupixDeck.ViewModels;
using System.Runtime.Versioning;

namespace LoupixDeck.Views;

public partial class InitSetup : Window
{
#if WINDOWS
    [SupportedOSPlatform("windows")]
#endif
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

    private void AllowClose()
    {
        _allowClose = true;
    }
}