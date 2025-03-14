using Avalonia.Media;
using LoupixDeck.LoupedeckDevice;
using LoupixDeck.LoupedeckDevice.Device;
using LoupixDeck.Utils;
using System.Collections.ObjectModel;

namespace LoupixDeck.Models;

public sealed class LoupedeckLiveS : LoupedeckBase
{
    public LoupedeckLiveS()
    {
        StartDeviceThread();

        SimpleButtons =
        [
            CreateSimpleButton("0", Colors.Blue, "System.PreviousPage"),
            CreateSimpleButton("1", Colors.Blue, string.Empty),
            CreateSimpleButton("2", Colors.Blue, string.Empty),
            CreateSimpleButton("3", Colors.Blue, "System.NextPage")
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

        TouchButtonPages = new ObservableCollection<TouchButtonPage>();
        CurrentTouchButtonPage = new TouchButton[15];

        TouchButtonPages.CollectionChanged += (s, e) =>
        {
            Console.WriteLine($"Collection changed: {e.Action}");
        };

        for (var i = 0; i < CurrentTouchButtonPage.Length; i++)
        {
            CurrentTouchButtonPage[i] = new TouchButton(i);
        }

        InitUpdateEvents();
    }

    public override void InitButtonEvents()
    {
        StaticDevice.Device.OnButton += OnSimpleButtonPress;
        StaticDevice.Device.OnTouch += OnTouchButtonPress;
        StaticDevice.Device.OnRotate += OnRotate;
    }

    public override SimpleButton CreateSimpleButton(string id, Color color, string command)
    {
        var button = new SimpleButton { Id = id, Command = string.Empty, ButtonColor = color };
        button.RenderedImage = BitmapHelper.RenderSimpleButtonImage(button, 90, 90);
        button.Command = command;
        button.ItemChanged += SimpleButtonChanged;
        return button;
    }

    public override void OnSimpleButtonPress(object sender, ButtonEventArgs e)
    {
        if (e.EventType != Constants.ButtonEventType.BUTTON_DOWN) return;

        var button = SimpleButtons.FirstOrDefault(b => b.Id == e.ButtonId);
        if (button != null)
        {
            if (Constants.SystemCommands.TryGetValue(button.Command, out var command))
            {
                ExceuteSystemCommand(command);
            }
            else
            {
                CommandRunner.ExecuteCommand(button.Command);
            }
        }
    }

    public override void OnTouchButtonPress(object sender, TouchEventArgs e)
    {
        if (e.EventType != Constants.TouchEventType.TOUCH_START) return;

        foreach (var touch in e.Touches)
        {
            var button = CurrentTouchButtonPage.FirstOrDefault(b => b.Index == touch.Target.Key);
            if (button == null) continue;

            StaticDevice.Device.Vibrate();

            if (Constants.SystemCommands.TryGetValue(button.Command, out var command))
            {
                ExceuteSystemCommand(command);
            }
            else
            {
                CommandRunner.ExecuteCommand(button.Command);
            }
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
            if (Constants.SystemCommands.TryGetValue(command, out var systemCommand))
            {
                ExceuteSystemCommand(systemCommand);
            }
            else
            {
                CommandRunner.ExecuteCommand(command);
            }
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

            // Changes need to be written back to original array.
            CopyBackTouchButtonData(button);
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

    public override void AddPage()
    {
        CurrentPageIndex++;


        var newPage = new TouchButtonPage(15);
        newPage.Page = TouchButtonPages.Count + 1;

        for (var i = 0; i < 15; i++)
        {
            newPage.TouchButtons[i] = new TouchButton(i)
            {
                Image = null,
                Command = $"Command {i}",
                BackColor = Colors.Black,
                TextColor = Colors.Lime,
                TextSize = 16,
                TextCentered = true
            };
        }
        
        TouchButtonPages.Add(newPage);

        ApplyPage(CurrentPageIndex);
    }

    public override void ExceuteSystemCommand(Constants.SystemCommand command)
    {
        switch (command)
        {
            case Constants.SystemCommand.NEXT_PAGE:
                NextPage();
                break;
            case Constants.SystemCommand.PREVIOUS_PAGE:
                PreviousPage();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), command, null);
        }
    }
}