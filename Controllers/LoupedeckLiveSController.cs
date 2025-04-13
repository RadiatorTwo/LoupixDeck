using LoupixDeck.LoupedeckDevice;
using LoupixDeck.Models;
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
public class LoupedeckLiveSController
{
    private readonly IDeviceService _deviceService;
    private readonly ICommandService _commandService;
    private readonly IPageManager _pageManager;
    private readonly IConfigService _configService;
    private readonly LoupedeckConfig _config;
    private readonly string _configPath;

    public LoupedeckLiveSController(
        IDeviceService deviceService,
        ICommandService commandService,
        IPageManager pageManager,
        IConfigService configService,
        LoupedeckConfig config)
    {
        _deviceService = deviceService;
        _commandService = commandService;
        _pageManager = pageManager;
        _configService = configService;
        _config = config;

        // Define the path to the configuration file (using FileDialogHelper or similar)
        _configPath = FileDialogHelper.GetConfigPath("config.json");

        // Load configuration at startup.
        // _config = _configService.LoadConfig<LoupedeckConfig>(_configPath) ?? new LoupedeckConfig();
    }

    public IPageManager PageManager => _pageManager;

    public LoupedeckConfig Config => _config;

    public void Initialize(string port = null, int baudrate = 0)
    {
        if (port != null)
            Config.DevicePort = port;

        if (baudrate > 0)
            Config.DeviceBaudrate = baudrate;

        // Start the device using the configuration
        _deviceService.StartDevice(_config.DevicePort, _config.DeviceBaudrate);

        // Register the button events.
        InitButtonEvents();

        // If no SimpleButtons are available, create standard buttons.
        if (_config.SimpleButtons == null || !_config.SimpleButtons.Any())
        {
            _config.SimpleButtons = new[]
            {
                CreateSimpleButton(Constants.ButtonType.BUTTON0, Avalonia.Media.Colors.Blue, "System.PreviousPage"),
                CreateSimpleButton(Constants.ButtonType.BUTTON1, Avalonia.Media.Colors.Blue,
                    "System.PreviousRotaryPage"),
                CreateSimpleButton(Constants.ButtonType.BUTTON2, Avalonia.Media.Colors.Blue, "System.NextRotaryPage"),
                CreateSimpleButton(Constants.ButtonType.BUTTON3, Avalonia.Media.Colors.Blue, "System.NextPage")
            };
        }

        if (_config.CurrentTouchButtonPage == null)
        {
            _config.CurrentTouchButtonPage = new TouchButtonPage(15);

            for (var i = 0; i < _config.CurrentTouchButtonPage.TouchButtons.Length; i++)
            {
                _config.CurrentTouchButtonPage.TouchButtons[i] = new TouchButton(i);
            }
        }

        if (_config.TouchButtonPages == null || _config.TouchButtonPages.Count == 0)
        {
            _pageManager.AddTouchButtonPage();
        }
        else
        {
            _pageManager.ApplyTouchPage(_config.CurrentTouchPageIndex);
        }

        if (_config.RotaryButtonPages == null || _config.RotaryButtonPages.Count == 0)
        {
            _pageManager.AddRotaryButtonPage();
        }

        // Register update events for buttons.
        InitUpdateEvents();

        // Update UI-based elements.
        _pageManager.RefreshSimpleButtons();
        _pageManager.RefreshTouchButtons();

        // Save the initial configuration.
        SaveConfig();
    }

    private void InitButtonEvents()
    {
        var device = _deviceService.Device;
        device.OnButton += OnSimpleButtonPress;
        device.OnTouch += OnTouchButtonPress;
        device.OnRotate += OnRotate;
    }

    public void OnSimpleButtonPress(object sender, ButtonEventArgs e)
    {
        if (e.EventType != Constants.ButtonEventType.BUTTON_DOWN)
            return;

        var button = _config.SimpleButtons.FirstOrDefault(b => b.Id == e.ButtonId);
        if (button != null)
        {
            _commandService.ExecuteCommand(button.Command);
        }
        else
        {
            switch (e.ButtonId)
            {
                case Constants.ButtonType.KNOB_TL:
                    _commandService.ExecuteCommand(_config.RotaryButtonPages[_config.CurrentRotaryPageIndex]
                        .RotaryButtons[0].Command);
                    break;
                case Constants.ButtonType.KNOB_CL:
                    _commandService.ExecuteCommand(_config.RotaryButtonPages[_config.CurrentRotaryPageIndex]
                        .RotaryButtons[1].Command);
                    break;
            }
        }
    }

    public void OnTouchButtonPress(object sender, TouchEventArgs e)
    {
        if (e.EventType != Constants.TouchEventType.TOUCH_START)
            return;

        foreach (var touch in e.Touches)
        {
            var button = _config.CurrentTouchButtonPage.TouchButtons.FirstOrDefault(b => b.Index == touch.Target.Key);
            if (button == null) continue;

            _commandService.ExecuteCommand(button.Command);
            _deviceService.Device.Vibrate();
        }
    }

    public void OnRotate(object sender, RotateEventArgs e)
    {
        string command = e.ButtonId switch
        {
            Constants.ButtonType.KNOB_TL => e.Delta < 0
                ? _config.RotaryButtonPages[_config.CurrentRotaryPageIndex].RotaryButtons[0].RotaryLeftCommand
                : _config.RotaryButtonPages[_config.CurrentRotaryPageIndex].RotaryButtons[0].RotaryRightCommand,
            Constants.ButtonType.KNOB_CL => e.Delta < 0
                ? _config.RotaryButtonPages[_config.CurrentRotaryPageIndex].RotaryButtons[1].RotaryLeftCommand
                : _config.RotaryButtonPages[_config.CurrentRotaryPageIndex].RotaryButtons[1].RotaryRightCommand,
            _ => null
        };

        if (!string.IsNullOrEmpty(command))
        {
            _commandService.ExecuteCommand(command);
        }
    }

    public SimpleButton CreateSimpleButton(Constants.ButtonType id, Avalonia.Media.Color color, string command)
    {
        var button = new SimpleButton
        {
            Id = id,
            Command = command,
            ButtonColor = color
        };
        button.RenderedImage = BitmapHelper.RenderSimpleButtonImage(button, 90, 90);
        button.ItemChanged += (s, e) =>
        {
            button.RenderedImage = BitmapHelper.RenderSimpleButtonImage(button, 90, 90);
            _deviceService.Device.SetButtonColor(button.Id, button.ButtonColor);
        };
        return button;
    }

    private void InitUpdateEvents()
    {
        foreach (var touchButton in _config.CurrentTouchButtonPage.TouchButtons)
        {
            touchButton.ItemChanged += (s, e) => TouchItemChanged(s, e);
        }

        foreach (var simpleButton in _config.SimpleButtons)
        {
            simpleButton.ItemChanged += (s, e) => SimpleButtonChanged(s, e);
        }
    }

    private void SimpleButtonChanged(object sender, EventArgs e)
    {
        if (sender is not SimpleButton button) return;
        
        button.RenderedImage = BitmapHelper.RenderSimpleButtonImage(button, 90, 90);
        _deviceService.Device.SetButtonColor(button.Id, button.ButtonColor);
    }

    private void TouchItemChanged(object sender, EventArgs e)
    {
        if (sender is not TouchButton item) return;
        
        var button = _config.CurrentTouchButtonPage.TouchButtons.FirstOrDefault(b => b.Index == item.Index);
            
        if (button == null) return;
            
        _deviceService.Device.DrawTouchButton(button);
        _pageManager.CopyBackTouchButtonData(button);
    }

    public void ApplyAllData()
    {
        var device = _deviceService.Device;
        foreach (var simpleButton in _config.SimpleButtons)
        {
            device.SetButtonColor(simpleButton.Id, simpleButton.ButtonColor);
        }

        foreach (var touchButton in _config.CurrentTouchButtonPage.TouchButtons)
        {
            device.DrawTouchButton(touchButton);
        }

        device.SetBrightness(_config.Brightness);
    }

    public void SaveConfig()
    {
        _pageManager.CopyCurrentTouchButtonsToPage();
        _configService.SaveConfig(_config, _configPath);
    }
}