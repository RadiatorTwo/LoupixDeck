using LoupixDeck.LoupedeckDevice.Device;
using LoupixDeck.Models;

namespace LoupixDeck.Services;

public interface IDeviceService
{
    LoupedeckLiveSDevice Device { get; }
    void StartDevice(string devicePort, int deviceBaudrate);
    Task ShowTemporaryTextButton(int index, string text, int displayDurationMilliseconds);
}

public class LoupedeckDeviceService : IDeviceService
{
    private readonly IElgatoController _elgatoController;
    private readonly LoupedeckConfig _config;
    private readonly AutoResetEvent _deviceCreatedEvent = new(false);

    public LoupedeckLiveSDevice Device { get; private set; }

    public LoupedeckDeviceService(IObsController obsController,
        IElgatoController elgatoController,
        ElgatoDevices elgatoDevices,
        LoupedeckConfig config)
    {
        _elgatoController = elgatoController;
        _config = config;

        obsController.Connect();

        _elgatoController.KeyLightFound += (_, light) =>
        {
            var checkDevice = elgatoDevices.KeyLights.FirstOrDefault(kl => kl.DisplayName == light.DisplayName);
            if (checkDevice != null)
            {
                elgatoDevices.RemoveKeyLight(checkDevice);
            }

            _elgatoController.InitDeviceAsync(light).GetAwaiter().GetResult();
            elgatoDevices.AddKeyLight(light);
        };
        _ = _elgatoController.ProbeForElgatoDevices();
    }

    public void StartDevice(string devicePort, int deviceBaudrate)
    {
        var deviceThread = new Thread(() =>
        {
            Device = new LoupedeckLiveSDevice(null, devicePort, deviceBaudrate);
            _deviceCreatedEvent.Set();
        })
        {
            IsBackground = true
        };
        deviceThread.Start();
        _deviceCreatedEvent.WaitOne();
    }

    public async Task ShowTemporaryTextButton(int index, string text, int displayDurationMilliseconds)
    {
        Device.DrawTextButton(index, text);

        await Task.Delay(displayDurationMilliseconds);

        Device.DrawTouchButton(_config.CurrentTouchButtonPage.TouchButtons[index], false);
    }
}