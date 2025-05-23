using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Platform;
using LoupixDeck.Utils;
using LoupixDeck.ViewModels;

namespace LoupixDeck.Views;

public partial class MainWindow : Window
{
    private static TrayIcon _trayIcon;
    private bool _isMinimizedToTray;

    // Static Commands
    private ICommand ShowCommand { get; }
    private ICommand QuitCommand { get; }

    private static MainWindow Instance { get; set; }
    
    public MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
        
        Instance = this;

        ShowCommand = new RelayCommand(() => Instance?.ShowFromTray());
        QuitCommand = new RelayCommand(() => Instance?.QuitApplication());

        // CreateTrayIcon();

        this.Closing += OnWindowClosing;
    }

    private void CreateTrayIcon()
    {
        if (_trayIcon == null)
        {
            _trayIcon = new TrayIcon
            {
                Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://LoupixDeck/Assets/logo.ico"))),
                ToolTipText = "LoupixDeck Tray",
                IsVisible = true,
                Menu = new NativeMenu()
            };

            var showMenuItem = new NativeMenuItem("Show") { Command = ShowCommand };
            var quitMenuItem = new NativeMenuItem("Quit") { Command = QuitCommand };

            _trayIcon.Menu?.Items.Add(showMenuItem);
            _trayIcon.Menu?.Items.Add(new NativeMenuItemSeparator());
            _trayIcon.Menu?.Items.Add(quitMenuItem);

            _trayIcon.Clicked += (sender, e) => ShowFromTray();
        }
    }

    private void OnWindowClosing(object sender, WindowClosingEventArgs e)
    {
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

            if (_trayIcon != null)
            {
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            CreateTrayIcon();

            Hide();
        });
    }

    private void ShowFromTray()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!_isMinimizedToTray) return;

            _isMinimizedToTray = false;
            Show();
            Activate();

            if (_trayIcon != null)
            {
                _trayIcon.IsVisible = false;
            }
        });
    }

    private void QuitApplication()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
        Environment.Exit(0);
    }
}