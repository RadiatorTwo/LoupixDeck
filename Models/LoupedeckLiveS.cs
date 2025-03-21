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
            CreateSimpleButton(Constants.ButtonType.BUTTON0, Colors.Blue, "System.PreviousPage"),
            CreateSimpleButton(Constants.ButtonType.BUTTON1, Colors.Blue, "System.PreviousRotaryPage"),
            CreateSimpleButton(Constants.ButtonType.BUTTON2, Colors.Blue, "System.NextRotaryPage"),
            CreateSimpleButton(Constants.ButtonType.BUTTON3, Colors.Blue, "System.NextPage")
        ];

        RotaryButtonPages = new ObservableCollection<RotaryButtonPage>();
        TouchButtonPages = new ObservableCollection<TouchButtonPage>();
        CurrentTouchButtonPage = new TouchButton[15];

        for (var i = 0; i < CurrentTouchButtonPage.Length; i++)
        {
            CurrentTouchButtonPage[i] = new TouchButton(i);
        }

        InitUpdateEvents();
    }

    public override void InitButtonEvents()
    {
        Device.OnButton += OnSimpleButtonPress;
        Device.OnTouch += OnTouchButtonPress;
        Device.OnRotate += OnRotate;
    }

    public override SimpleButton CreateSimpleButton(Constants.ButtonType id, Color color, string command)
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
            RunCommand(button.Command);
        }
        else
        {
            switch (e.ButtonId)
            {
                case Constants.ButtonType.KNOB_TL:
                    RunCommand(RotaryButtonPages[CurrentRotaryPageIndex].RotaryButtons[0].Command);
                    break;
                case Constants.ButtonType.KNOB_CL:
                    RunCommand(RotaryButtonPages[CurrentRotaryPageIndex].RotaryButtons[1].Command);
                    break;
            }
        }
    }

    private void RunCommand(string command)
    {
        if (Constants.SystemCommands.TryGetValue(command, out var systemCommand))
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { ExceuteSystemCommand(systemCommand); });
        }
        else
        {
            CommandRunner.EnqueueCommand(command);
        }
    }

    public override void OnTouchButtonPress(object sender, TouchEventArgs e)
    {
        if (e.EventType != Constants.TouchEventType.TOUCH_START) return;

        foreach (var touch in e.Touches)
        {
            var button = CurrentTouchButtonPage.FirstOrDefault(b => b.Index == touch.Target.Key);
            if (button == null) continue;

            Device.Vibrate();

            if (Constants.SystemCommands.TryGetValue(button.Command, out var command))
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => { ExceuteSystemCommand(command); });
            }
            else
            {
                CommandRunner.EnqueueCommand(button.Command);
            }
        }
    }

    public override void OnRotate(object sender, RotateEventArgs e)
    {
        var command = e.ButtonId switch
        {
            Constants.ButtonType.KNOB_TL => e.Delta < 0 ? RotaryButtonPages[CurrentRotaryPageIndex].RotaryButtons[0].RotaryLeftCommand : RotaryButtonPages[CurrentRotaryPageIndex].RotaryButtons[0].RotaryRightCommand,
            Constants.ButtonType.KNOB_CL => e.Delta < 0 ? RotaryButtonPages[CurrentRotaryPageIndex].RotaryButtons[1].RotaryLeftCommand : RotaryButtonPages[CurrentRotaryPageIndex].RotaryButtons[1].RotaryRightCommand,
            _ => null
        };

        if (string.IsNullOrEmpty(command)) return;
        
        if (Constants.SystemCommands.TryGetValue(command, out var systemCommand))
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { ExceuteSystemCommand(systemCommand); });
        }
        else
        {
            CommandRunner.EnqueueCommand(command);
        }
    }

    protected override void SimpleButtonChanged(object sender, EventArgs e)
    {
        if (sender is not SimpleButton button) return;
        button.RenderedImage = BitmapHelper.RenderSimpleButtonImage(button, 90, 90);
        Device.SetButtonColor(button.Id, button.ButtonColor);
    }

    protected override void TouchItemChanged(object sender, EventArgs e)
    {
        if (sender is not TouchButton item) return;

        var button = CurrentTouchButtonPage.FirstOrDefault(b => b.Index == item.Index);
        if (button != null)
        {
            Device.DrawTouchButton(button);

            // Changes need to be written back to original array.
            CopyBackTouchButtonData(button);
        }
    }

    public override void StartDeviceThread()
    {
        var deviceThread = new Thread(() =>
        {
            // Create instance of the device on this thread to ensure all events run here
            Device = new LoupedeckLiveSDevice();

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
            Device.SetButtonColor(simpleButton.Id, simpleButton.ButtonColor);
        }

        foreach (var touchButton in CurrentTouchButtonPage)
        {
            Device.DrawTouchButton(touchButton);
        }

        Device.SetBrightness(Brightness);
    }
    
    public override void AddRotaryButtonPage()
    {
        var newPage = new RotaryButtonPage(2)
        {
            Page = RotaryButtonPages.Count + 1
        };

        RotaryButtonPages.Add(newPage);
        CurrentRotaryPageIndex = RotaryButtonPages.Count - 1;
    }
    
    public override void DeleteRotaryButtonPage()
    {
        if (RotaryButtonPages.Count == 1)
            return;
        
        // Remove Page and Reorder remaining pages.
        RotaryButtonPages.RemoveAt(CurrentRotaryPageIndex);

        var counter = 0;
        foreach (var t in RotaryButtonPages)
        {
            counter++;
            t.Page = counter;
        }

        if (CurrentRotaryPageIndex < RotaryButtonPages.Count) 
        {
            ApplyRotaryPage(CurrentRotaryPageIndex);
        }
        else
        {
            ApplyRotaryPage(RotaryButtonPages.Count - 1);
        }
    }
    
    public override void AddTouchButtonPage()
    {
        var newPage = new TouchButtonPage(15)
        {
            Page = TouchButtonPages.Count + 1
        };

        for (var i = 0; i < 15; i++)
        {
            newPage.TouchButtons[i] = new TouchButton(i);
        }

        TouchButtonPages.Add(newPage);

        ApplyTouchPage(CurrentTouchPageIndex);
    }

    public override void DeleteTouchButtonPage()
    {
        if (TouchButtonPages.Count == 1)
            return;
        
        // Remove Page and Reorder remaining pages.
        TouchButtonPages.RemoveAt(CurrentTouchPageIndex);

        var counter = 0;
        foreach (var t in TouchButtonPages)
        {
            counter++;
            t.Page = counter;
        }

        if (CurrentTouchPageIndex < TouchButtonPages.Count) 
        {
            ApplyTouchPage(CurrentTouchPageIndex);
        }
        else
        {
            ApplyTouchPage(TouchButtonPages.Count - 1);
        }
    }

    public override void ExceuteSystemCommand(Constants.SystemCommand command)
    {
        switch (command)
        {
            case Constants.SystemCommand.NEXT_PAGE:
                NextTouchPage();
                break;
            case Constants.SystemCommand.PREVIOUS_PAGE:
                PreviousTouchPage();
                break;
            case Constants.SystemCommand.NEXT_ROT_PAGE:
                NextRotaryPage();
                break;
            case Constants.SystemCommand.PREVIOUS_ROT_PAGE:
                PreviousRotaryPage();
                break;
            case Constants.SystemCommand.OBS_VIRTUAL_CAM:
                Obs.ToggleVirtualCamera();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), command, null);
        }
    }
}