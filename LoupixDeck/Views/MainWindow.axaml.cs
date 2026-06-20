using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.Input;
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
    private IRelayCommand ShowCommand => field ??= Relay.Create(() => Instance?.ShowFromTray());
    private IRelayCommand QuitCommand => field ??= Relay.Create(() => Instance?.QuitApplication());
    private IRelayCommand ToggleDeviceCommand => field ??= Relay.Create(() =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                Instance?.ViewModel?.SelectedDevice?.ToggleDeviceStateCommand?.Execute(null)));

    private static MainWindow Instance { get; set; }

    public MainShellViewModel ViewModel => DataContext as MainShellViewModel;

    private MainShellViewModel _shell;

    public MainWindow()
    {
        InitializeComponent();

        Instance = this;

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
        if (_shell != null) _shell.PropertyChanged -= OnShellPropertyChanged;
        _shell = DataContext as MainShellViewModel;
        if (_shell != null) _shell.PropertyChanged += OnShellPropertyChanged;

        UpdateDeviceLayout();
    }

    private void OnShellPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainShellViewModel.SelectedDevice))
            UpdateDeviceLayout();
    }

    /// <summary>Swap the DeviceLayoutHost to the selected device's layout, with that
    /// device's view model as its DataContext (the layout binds MainWindowViewModel
    /// members, but the window DataContext is the shell).</summary>
    private void UpdateDeviceLayout()
    {
        var host = this.FindControl<ContentControl>("DeviceLayoutHost");
        if (host == null) return;

        var vm = _shell?.SelectedDevice;
        if (vm == null)
        {
            host.Content = null;
            return;
        }

        host.Content = vm.DeviceSlug switch
        {
            // The Loupedeck Live is hardware-identical to the Razer Stream Controller
            // (480×270 split display, 4×3 grid, 2 side strips, 6 knobs, 8 LED buttons),
            // so it reuses the Razer editor layout. The on-screen chassis art is the
            // Razer body until a dedicated Loupedeck Live SVG is added.
            "razer-stream-controller" or "loupedeck-live" => new RazerStreamControllerLayout { DataContext = vm },
            "loupedeck-ct" => new LoupedeckCtLayout { DataContext = vm },
            _ => new LoupedeckLiveSLayout { DataContext = vm }
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

        var behavior = ViewModel?.SelectedDevice?.LoupedeckController?.Config?.CloseButtonBehavior
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

    /// <summary>
    /// Toggles between visible and tray-minimized. Used by the System.ToggleWindow
    /// command so an external trigger (button on the device, CLI) can bring the
    /// window back without going through the tray icon.
    /// </summary>
    internal void ToggleVisibility()
    {
        if (IsVisible) MinimizeToTray();
        else ShowFromTray();
    }

    private bool _isQuitting;

    internal void QuitApplication()
    {
        _isQuitting = true;
        _trayIcon?.Dispose();
        _trayIcon = null;

        // Cleanly shut down every device before the process dies. Critically this stops a
        // running screensaver and closes the serial port, so we never cut a full-screen
        // framebuffer write mid-stream — an interrupted write leaves the device's protocol
        // desynced and makes the next launch's handshake time out until a power-cycle.
        try
        {
            if (Program.AppServices?.GetService(typeof(Services.IDeviceHostRegistry))
                is Services.IDeviceHostRegistry hostRegistry)
            {
                foreach (var host in hostRegistry.Hosts)
                {
                    try { host.Controller.Shutdown(); }
                    catch { /* best effort — never block shutdown */ }
                }
            }
        }
        catch
        {
            // best effort — never block shutdown
        }

        // Give the (shared, root-resident) loaded plugins a chance to shut down
        // cleanly (close connections, stop poll loops) before the process exits.
        try
        {
            (Program.AppServices?.GetService(typeof(Services.Plugins.IPluginManager))
                as Services.Plugins.IPluginManager)?.ShutdownAll();
        }
        catch
        {
            // best effort — never block shutdown
        }

        Environment.Exit(0);
    }
}
