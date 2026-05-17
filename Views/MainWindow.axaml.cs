using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Platform;
using LoupixDeck.Models;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels;
using LoupixDeck.Views.Devices;

namespace LoupixDeck.Views;

public partial class MainWindow : Window
{
    private static TrayIcon _trayIcon;
    private bool _isMinimizedToTray;

    // Static Commands
    private ICommand ShowCommand { get; }
    private ICommand QuitCommand { get; }
    private ICommand ToggleDeviceCommand { get; }

    private static MainWindow Instance { get; set; }

    public MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();

        Instance = this;

        ShowCommand = new RelayCommand(() => Instance?.ShowFromTray());
        QuitCommand = new RelayCommand(() => Instance?.QuitApplication());
        ToggleDeviceCommand = new RelayCommand(() =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                Instance?.ViewModel?.ToggleDeviceStateCommand?.Execute(null)));

        CreateTrayIcon();

        this.Closing += OnWindowClosing;
        this.DataContextChanged += OnDataContextChanged;
    }

    /// <summary>
    /// Pick the device-specific UserControl when DI hands us a VM. The child
    /// inherits DataContext, so its existing LoupedeckController.Config bindings
    /// resolve unchanged. Unknown slugs fall through to Live S to keep something
    /// rendered rather than a blank window.
    /// </summary>
    private void OnDataContextChanged(object sender, System.EventArgs e)
    {
        var host = this.FindControl<ContentControl>("DeviceLayoutHost");
        if (host == null || DataContext is not MainWindowViewModel vm) return;

        host.Content = vm.DeviceSlug switch
        {
            "razer-stream-controller" => new RazerStreamControllerLayout(),
            _ => new LoupedeckLiveSLayout()
        };
    }

    /// <summary>
    /// Called by App.OnViewModelCreated before Show() to mark the window as
    /// already-minimized when StartMinimizedToTray is on. Avoids the brief
    /// Show→Hide flash we'd get if we hid the window after it was shown.
    /// </summary>
    internal void MarkStartedMinimized()
    {
        _isMinimizedToTray = true;
    }

    private void CreateTrayIcon()
    {
        if (_trayIcon != null) return;

        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://LoupixDeck/Assets/logo.ico"))),
            ToolTipText = "LoupixDeck",
            IsVisible = true,
            Menu = new NativeMenu()
        };

        var showMenuItem = new NativeMenuItem("Show") { Command = ShowCommand };
        var toggleMenuItem = new NativeMenuItem("Toggle device on/off") { Command = ToggleDeviceCommand };
        var quitMenuItem = new NativeMenuItem("Quit") { Command = QuitCommand };

        _trayIcon.Menu?.Items.Add(showMenuItem);
        _trayIcon.Menu?.Items.Add(toggleMenuItem);
        _trayIcon.Menu?.Items.Add(new NativeMenuItemSeparator());
        _trayIcon.Menu?.Items.Add(quitMenuItem);

        _trayIcon.Clicked += (_, _) => ShowFromTray();
    }

    private void OnWindowClosing(object sender, WindowClosingEventArgs e)
    {
        // Already on the way out (via tray Quit / hamburger Quit) — let it close.
        if (_isQuitting) return;

        var behavior = (DataContext as MainWindowViewModel)?.LoupedeckController?.Config?.CloseButtonBehavior
                       ?? CloseButtonBehavior.MinimizeToTray;

        if (behavior == CloseButtonBehavior.Quit)
        {
            // Let the window close naturally, then exit the process so the
            // classic-desktop lifetime doesn't keep us alive with no MainWindow.
            QuitApplication();
            return;
        }

        if (!_isMinimizedToTray)
        {
            e.Cancel = true;
            MinimizeToTray();
        }
    }

    private void MinimizeToTray()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_isMinimizedToTray) return;
            _isMinimizedToTray = true;
            Hide();
        });
    }

    private void ShowFromTray()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _isMinimizedToTray = false;
            Show();
            WindowState = WindowState.Normal;
            Activate();
        });
    }

    private bool _isQuitting;

    internal void QuitApplication()
    {
        _isQuitting = true;
        _trayIcon?.Dispose();
        _trayIcon = null;
        Environment.Exit(0);
    }
}
