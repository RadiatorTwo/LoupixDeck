using System.ComponentModel;
using LoupixDeck.LoupedeckDevice;
using LoupixDeck.Models;
using LoupixDeck.Models.Extensions;
using LoupixDeck.Services;
using LoupixDeck.Utils;

namespace LoupixDeck.Controllers;

/// <summary>
/// This controller orchestrates the collaboration of the services:
/// - It loads or saves the configuration,
/// - starts the device,
/// - registers the device events and
/// - forwards the UI events to the corresponding services.
/// </summary>
public class LoupedeckLiveSController(
    IDeviceService deviceService,
    ICommandService commandService,
    IPageManager pageManager,
    IConfigService configService,
    LoupedeckConfig config)
{
    private readonly string _configPath = FileDialogHelper.GetConfigPath("config.json");

    public IPageManager PageManager => pageManager;

    public LoupedeckConfig Config => config;

    public async Task Initialize(string port = null, int baudrate = 0)
    {
        if (port != null)
            Config.DevicePort = port;

        if (baudrate > 0)
            Config.DeviceBaudrate = baudrate;

        // Start the device using the configuration
        deviceService.StartDevice(config.DevicePort, config.DeviceBaudrate);

        // Migrate old wallpaper configuration to first page if needed
        if (config.Wallpaper != null && config.TouchButtonPages != null && config.TouchButtonPages.Count > 0)
        {
            if (config.TouchButtonPages[0].Wallpaper == null)
            {
                config.TouchButtonPages[0].Wallpaper = config.Wallpaper;
                config.TouchButtonPages[0].WallpaperOpacity = config.WallpaperOpacity;
                config.Wallpaper = null; // Clear old property
                config.WallpaperOpacity = 0;
            }
        }

        pageManager.OnTouchPageChanged += OnTouchPageChanged;

        // Subscribe to page property changes for wallpaper updates
        foreach (var page in config.TouchButtonPages)
        {
            page.PropertyChanged += TouchButtonPageOnPropertyChanged;
        }

        // Subscribe to collection changes to handle newly added pages
        config.TouchButtonPages.CollectionChanged += TouchButtonPagesOnCollectionChanged;

        config.SimpleButtons =
        [
            await CreateSimpleButton(Constants.ButtonType.BUTTON0, Avalonia.Media.Colors.Blue, "System.PreviousPage"),
            await CreateSimpleButton(Constants.ButtonType.BUTTON1, Avalonia.Media.Colors.Blue, "System.PreviousRotaryPage"),
            await CreateSimpleButton(Constants.ButtonType.BUTTON2, Avalonia.Media.Colors.Blue, "System.NextRotaryPage"),
            await CreateSimpleButton(Constants.ButtonType.BUTTON3, Avalonia.Media.Colors.Blue, "System.NextPage")
        ];

        if (config.RotaryButtonPages == null || config.RotaryButtonPages.Count == 0)
        {
            pageManager.AddRotaryButtonPage(true);
        }
        else
        {
            // Existing config Init always page 0.
            config.CurrentRotaryPageIndex = 0;
            pageManager.ApplyRotaryPage(config.CurrentRotaryPageIndex, true);
        }

        if (config.TouchButtonPages == null || config.TouchButtonPages.Count == 0)
        {
            await pageManager.AddTouchButtonPage(true);
        }
        else
        {
            // Existing config Init always page 0.
            config.CurrentTouchPageIndex = 0;
            await pageManager.ApplyTouchPage(config.CurrentTouchPageIndex, true);

            // With an existing config, we need to apply the item changed event to the current Touch Button Page
            foreach (var touchButton in config.CurrentTouchButtonPage.TouchButtons)
            {
                touchButton.ItemChanged += TouchItemChanged;
            }

            foreach (var touchButton in config.CurrentTouchButtonPage.TouchButtons)
            {
                await deviceService.Device.DrawTouchButton(touchButton, config, true, 5);
            }
        }

        config.CurrentRotaryButtonPage.Selected = true;
        config.CurrentTouchButtonPage.Selected = true;

        config.PropertyChanged += ConfigOnPropertyChanged;

        await deviceService.Device.SetBrightness(config.Brightness / 100.0);

        InitButtonEvents();

        // Save the initial configuration.
        SaveConfig();

        await Task.CompletedTask;
    }

    private void InitButtonEvents()
    {
        var device = deviceService.Device;
        device.OnButton += OnSimpleButtonPress;
        device.OnTouch += OnTouchButtonPress;
        device.OnRotate += OnRotate;
    }

    private void OnSimpleButtonPress(object sender, ButtonEventArgs e)
    {
        if (e.EventType != Constants.ButtonEventType.BUTTON_DOWN)
            return;

        var button = config.SimpleButtons.FirstOrDefault(b => b.Id == e.ButtonId);
        if (button != null)
        {
            commandService.ExecuteCommand(button.Command).GetAwaiter().GetResult();
        }
        else
        {
            switch (e.ButtonId)
            {
                case Constants.ButtonType.KNOB_TL:
                    commandService.ExecuteCommand(config.RotaryButtonPages[config.CurrentRotaryPageIndex]
                        .RotaryButtons[0].Command).GetAwaiter().GetResult();
                    break;
                case Constants.ButtonType.KNOB_CL:
                    commandService.ExecuteCommand(config.RotaryButtonPages[config.CurrentRotaryPageIndex]
                        .RotaryButtons[1].Command).GetAwaiter().GetResult();
                    break;
            }
        }
    }

    private void OnTouchButtonPress(object sender, TouchEventArgs e)
    {
        if (e.EventType != Constants.TouchEventType.TOUCH_START)
            return;

        foreach (var touch in e.Touches)
        {
            var button = config.CurrentTouchButtonPage.TouchButtons.FindByIndex(touch.Target.Key);
            if (button == null) continue;

            commandService.ExecuteCommand(button.Command).GetAwaiter().GetResult();
            
            deviceService.Device.Vibrate();
        }
    }

    private void OnRotate(object sender, RotateEventArgs e)
    {
        string command = e.ButtonId switch
        {
            Constants.ButtonType.KNOB_TL => e.Delta < 0
                ? config.RotaryButtonPages[config.CurrentRotaryPageIndex].RotaryButtons[0].RotaryLeftCommand
                : config.RotaryButtonPages[config.CurrentRotaryPageIndex].RotaryButtons[0].RotaryRightCommand,
            Constants.ButtonType.KNOB_CL => e.Delta < 0
                ? config.RotaryButtonPages[config.CurrentRotaryPageIndex].RotaryButtons[1].RotaryLeftCommand
                : config.RotaryButtonPages[config.CurrentRotaryPageIndex].RotaryButtons[1].RotaryRightCommand,
            _ => null
        };

        if (!string.IsNullOrEmpty(command))
        {
            commandService.ExecuteCommand(command).GetAwaiter().GetResult();
        }
    }

    private void OnTouchPageChanged(int oldIndex, int newIndex)
    {
        if (oldIndex >= 0 && oldIndex < config.TouchButtonPages.Count && config.TouchButtonPages[oldIndex] != null)
        {
            foreach (var touchButton in config.TouchButtonPages[oldIndex].TouchButtons)
            {
                touchButton.ItemChanged -= TouchItemChanged;
            }
        }

        if (newIndex >= 0 && newIndex < config.TouchButtonPages.Count && config.TouchButtonPages[newIndex] != null)
        {
            foreach (var touchButton in config.TouchButtonPages[newIndex].TouchButtons)
            {
                touchButton.ItemChanged += TouchItemChanged;
            }
        }
    }

    private void TouchButtonPagesOnCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // Subscribe to property changes for newly added pages
        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is TouchButtonPage page)
                {
                    page.PropertyChanged += TouchButtonPageOnPropertyChanged;
                }
            }
        }

        // Unsubscribe from property changes for removed pages
        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is TouchButtonPage page)
                {
                    page.PropertyChanged -= TouchButtonPageOnPropertyChanged;
                }
            }
        }
    }

    private async void TouchButtonPageOnPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (sender is not TouchButtonPage page) return;

        // Only redraw if this is the current page and wallpaper properties changed
        if (page != config.CurrentTouchButtonPage) return;

        _propertyChangedCts?.Cancel();
        _propertyChangedCts = new CancellationTokenSource();
        var token = _propertyChangedCts.Token;

        try
        {
            switch (e.PropertyName)
            {
                case nameof(TouchButtonPage.Wallpaper):
                case nameof(TouchButtonPage.WallpaperOpacity):
                    await Task.Delay(100, token); // Debounce
                    foreach (var touchButton in config.CurrentTouchButtonPage.TouchButtons)
                    {
                        await deviceService.Device.DrawTouchButton(touchButton, config, true, 5);
                        await Task.Delay(0, token);
                    }
                    break;
            }
        }
        catch (TaskCanceledException)
        {
            // ignore canceled Tasks
        }
    }

    private async void TouchItemChanged(object sender, EventArgs e)
    {
        if (sender is not TouchButton item) return;

        var button = config.CurrentTouchButtonPage.TouchButtons.FirstOrDefault(b => b.Index == item.Index);

        if (button == null) return;

        await deviceService.Device.DrawTouchButton(button, config, true, 5);
    }

    private async Task<SimpleButton> CreateSimpleButton(Constants.ButtonType id, Avalonia.Media.Color color,
        string command)
    {
        var button = config.SimpleButtons.FindById(id) ?? new SimpleButton
        {
            Id = id,
            Command = command,
            ButtonColor = color
        };

        Avalonia.Threading.Dispatcher.UIThread.Invoke(() =>
        {
            button.RenderedImage = BitmapHelper.RenderSimpleButtonImage(button, 90, 90);
        });

        button.ItemChanged += SimpleButtonChanged;

        await deviceService.Device.SetButtonColor(id, button.ButtonColor);

        return button;
    }

    private async void SimpleButtonChanged(object sender, EventArgs e)
    {
        if (sender is not SimpleButton button) return;

        button.RenderedImage = BitmapHelper.RenderSimpleButtonImage(button, 90, 90);
        await deviceService.Device.SetButtonColor(button.Id, button.ButtonColor);
    }

    public void SaveConfig()
    {
        configService.SaveConfig(config, _configPath);
    }

    private CancellationTokenSource _propertyChangedCts;
    
    private async void ConfigOnPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        _propertyChangedCts?.Cancel();
        _propertyChangedCts = new CancellationTokenSource();
        var token = _propertyChangedCts.Token;

        try
        {
            switch (e.PropertyName)
            {
                case nameof(LoupedeckConfig.Brightness):
                    await Task.Delay(100, token); // Debounce
                    await deviceService.Device.SetBrightness(config.Brightness / 100.0);
                    break;
            }
        }
        catch (TaskCanceledException)
        {
            // ignore canceled Tasks
        }
    }
}