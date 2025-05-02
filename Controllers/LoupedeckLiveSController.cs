using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using LoupixDeck.LoupedeckDevice;
using LoupixDeck.Models;
using LoupixDeck.Models.Extensions;
using LoupixDeck.Services;
using LoupixDeck.Utils;
using SkiaSharp;

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

        pageManager.OnTouchPageChanged += OnTouchPageChanged;

        config.SimpleButtons =
        [
            await CreateSimpleButton(Constants.ButtonType.BUTTON0, Avalonia.Media.Colors.Blue, "System.PreviousPage"),
            await CreateSimpleButton(Constants.ButtonType.BUTTON1, Avalonia.Media.Colors.Blue,
                "System.PreviousRotaryPage"),
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
                await deviceService.Device.DrawTouchButton(touchButton, true, config.Wallpaper, 5);
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
            commandService.ExecuteCommand(button.Command);
        }
        else
        {
            switch (e.ButtonId)
            {
                case Constants.ButtonType.KNOB_TL:
                    commandService.ExecuteCommand(config.RotaryButtonPages[config.CurrentRotaryPageIndex]
                        .RotaryButtons[0].Command);
                    break;
                case Constants.ButtonType.KNOB_CL:
                    commandService.ExecuteCommand(config.RotaryButtonPages[config.CurrentRotaryPageIndex]
                        .RotaryButtons[1].Command);
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

            commandService.ExecuteCommand(button.Command);
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
            commandService.ExecuteCommand(command);
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

    private async void TouchItemChanged(object sender, EventArgs e)
    {
        if (sender is not TouchButton item) return;

        var button = config.CurrentTouchButtonPage.TouchButtons.FirstOrDefault(b => b.Index == item.Index);

        if (button == null) return;

        await deviceService.Device.DrawTouchButton(button, true, config.Wallpaper, 5);
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

    private async void ConfigOnPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(LoupedeckConfig.Brightness):
                await deviceService.Device.SetBrightness(config.Brightness / 100.0);
                break;

            case nameof(LoupedeckConfig.Wallpaper):
                foreach (var touchButton in config.CurrentTouchButtonPage.TouchButtons)
                {
                    await deviceService.Device.DrawTouchButton(touchButton, true, config.Wallpaper, 5);
                }

                break;

            case nameof(LoupedeckConfig.VideoPath):
                StartFfmpegReader();
                StartFrameProcessor();
                break;
        }
    }

    private async Task ProcessFrame(byte[] frame)
    {
        var bitmap = CreateBitmapFromRgb24(frame, 480, 270);
        await deviceService.Device.DrawScreen("center", bitmap.ToRenderTargetBitmap());
    }

    private readonly BlockingCollection<byte[]> _frameQueue = new(1); // max 1 Frame im Speicher

    private byte[] _frontBuffer;
    private byte[] _backBuffer;
    private readonly Lock _bufferLock = new();

    private void StartFfmpegReader()
    {
        Task.Run(() =>
        {
            using var ffmpeg = Process.Start(new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-i \"{config.VideoPath}\" -f rawvideo -r 30 -pix_fmt bgr0 -vf scale=480:270 -",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (ffmpeg == null) return;
            
            var stream = ffmpeg.StandardOutput.BaseStream;
            const int frameSize = 480 * 270 * 4;
            var buffer = new byte[frameSize];

            while (!ffmpeg.HasExited)
            {
                var read = 0;
                while (read < frameSize)
                {
                    var r = stream.Read(buffer, read, frameSize - read);
                    if (r <= 0) return;
                    read += r;
                }

                // Achtung: blockiert, wenn noch nicht verarbeitet
                _frameQueue.Add(buffer.ToArray());
            }
        });
    }

    private void SwapBuffers()
    {
        (_frontBuffer, _backBuffer) = (_backBuffer, _frontBuffer);
    }

    private void StartFrameProcessor()
    {
        Task.Run(async () =>
        {
            const int frameDurationMs = 1000 / 24;
            var stopwatch = Stopwatch.StartNew();

            while (true)
            {
                var frame = _frameQueue.Take(); // wartet auf neuen Frame

                var before = stopwatch.ElapsedMilliseconds;
                await ProcessFrame(frame);

                var after = stopwatch.ElapsedMilliseconds;
                var elapsed = after - before;

                var delay = Math.Max(0, frameDurationMs - (int)elapsed);
                await Task.Delay(delay);
            }
        });
    }

    private static SKBitmap CreateBitmapFromRgb24(byte[] rgbData, int width, int height)
    {
        var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var ptr = bitmap.GetPixels();

        Marshal.Copy(rgbData, 0, ptr, rgbData.Length);

        return bitmap;
    }
}