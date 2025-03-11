using Avalonia.Media;
using LoupixDeck.LoupedeckDevice;
using LoupixDeck.LoupedeckDevice.Device;
using LoupixDeck.Utils;

namespace LoupixDeck.Models;

public sealed class LoupedeckLiveS : LoupedeckBase
{
    public LoupedeckLiveS()
    {
        TouchButtonPages = new List<TouchButton[]>();

        TouchButtonPages.Add(Enumerable.Range(0, 15)
            .Select(index => new TouchButton(index)
            {
                Image = null,
                Command = $"Command {index}",
                BackColor = Colors.Black,
                TextColor = Colors.Lime,
                TextSize = 16,
                TextCentered = true
            })
            .ToArray());

        CurrentTouchButtonPage = new TouchButton[15];
        ApplyPage(0);

        SimpleButtons =
        [
            CreateSimpleButton("0", Colors.Blue),
            CreateSimpleButton("1", Colors.Blue),
            CreateSimpleButton("2", Colors.Blue),
            CreateSimpleButton("3", Colors.Blue)
        ];

        RotaryButtons =
        [
            new RotaryButton()
            {
                RotaryLeftCommand = "wpctl set-volume 33 1%-",
                RotaryRightCommand = "wpctl set-volume 33 1%+"
            },
            new RotaryButton()
        ];

        StartDeviceThread();
    }

    public override void InitButtonEvents()
    {
        StaticDevice.Device.OnButton += OnButton;
        StaticDevice.Device.OnTouch += OnButtonTouch;
        StaticDevice.Device.OnRotate += OnRotate;
    }

    public override SimpleButton CreateSimpleButton(string id, Color color)
    {
        var button = new SimpleButton { Id = id, Command = string.Empty, ButtonColor = color };
        button.RenderedImage = BitmapHelper.RenderSimpleButtonImage(button, 90, 90);
        button.ItemChanged += SimpleButtonChanged;
        return button;
    }

    public override void OnButton(object sender, ButtonEventArgs e)
    {
        if (e.EventType != Constants.ButtonEventType.BUTTON_DOWN) return;

        var button = SimpleButtons.FirstOrDefault(b => b.Id == e.ButtonId);
        if (button != null)
        {
            CommandRunner.ExecuteCommand(button.Command);
        }
    }

    public override void OnButtonTouch(object sender, TouchEventArgs e)
    {
        if (e.EventType != Constants.TouchEventType.TOUCH_START) return;
        
        foreach (var touch in e.Touches)
        {
            var button = CurrentTouchButtonPage.FirstOrDefault(b => b.Index == touch.Target.Key);
            if (button == null) continue;

            StaticDevice.Device.Vibrate();
            CommandRunner.ExecuteCommand(button.Command);
        }
    }

    public override void OnRotate(object sender, RotateEventArgs e)
    {
        var command = e.ButtonId switch
        {
            "knobTL" => e.Delta < 0 ? RotaryButtons[0].RotaryLeftCommand : RotaryButtons[0].RotaryRightCommand,
            "knobCL" => e.Delta < 0 ? RotaryButtons[1].RotaryLeftCommand : RotaryButtons[1].RotaryRightCommand,
            _ => null
        };

        if (!string.IsNullOrEmpty(command))
        {
            CommandRunner.ExecuteCommand(command);
        }
    }

    public override void SimpleButtonChanged(object sender, EventArgs e)
    {
        if (sender is not SimpleButton button) return;
        button.RenderedImage = BitmapHelper.RenderSimpleButtonImage(button, 90, 90);
        StaticDevice.Device.SetButtonColor(button.Id, button.ButtonColor);
    }

    public override void TouchItemChanged(object sender, EventArgs e)
    {
        if (sender is not TouchButton item) return;

        var button = CurrentTouchButtonPage.FirstOrDefault(b => b.Index == item.Index);
        if (button != null)
        {
            StaticDevice.Device.DrawTouchButton(button);
        }
    }

    public override void StartDeviceThread()
    {
        var deviceThread = new Thread(() =>
        {
            // Create instance of the device on this thread to ensure all events run here
            StaticDevice.Device = new LoupedeckLiveSDevice();

            // Signal that the instance has been created
            DeviceCreatedEvent.Set();

            InitButtonEvents();
        })
        {
            IsBackground = true
        };

        deviceThread.Start();
        DeviceCreatedEvent.WaitOne();
    }

    public override void ApplyAllData()
    {
        foreach (var simpleButton in SimpleButtons)
        {
            StaticDevice.Device.SetButtonColor(simpleButton.Id, simpleButton.ButtonColor);
        }

        foreach (var touchButton in CurrentTouchButtonPage)
        {
            StaticDevice.Device.DrawTouchButton(touchButton);
        }

        StaticDevice.Device.SetBrightness(Brightness);
    }
}