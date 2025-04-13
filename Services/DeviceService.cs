using LoupixDeck.LoupedeckDevice.Device;
using LoupixDeck.Models;

namespace LoupixDeck.Services;

public interface IDeviceService
{
    LoupedeckLiveSDevice Device { get; }
    void StartDevice(string devicePort, int deviceBaudrate);
}

public class LoupedeckDeviceService : IDeviceService
{
    private readonly IObsController _obsController;
    private readonly IDBusController _dbusController;
    private readonly IElgatoController _elgatoController;
    private readonly ElgatoDevices _elgatoDevices;
    private readonly ICommandRunner _commandRunner;
    private readonly AutoResetEvent _deviceCreatedEvent = new AutoResetEvent(false);

    public LoupedeckLiveSDevice Device { get; private set; }

    public LoupedeckDeviceService(IObsController obsController,
        IDBusController dbusController,
        IElgatoController elgatoController,
        ElgatoDevices elgatoDevices,
        ICommandRunner commandRunner)
    {
        _obsController = obsController;
        _dbusController = dbusController;
        _elgatoController = elgatoController;
        _elgatoDevices = elgatoDevices;
        _commandRunner = commandRunner;

        _obsController.Connect();

        _elgatoController.KeyLightFound += (_, light) =>
        {
            var checkDevice = _elgatoDevices.KeyLights.FirstOrDefault(kl => kl.DisplayName == light.DisplayName);
            if (checkDevice != null)
            {
                _elgatoDevices.RemoveKeyLight(checkDevice);
            }

            _elgatoController.InitDeviceAsync(light).GetAwaiter().GetResult();
            _elgatoDevices.AddKeyLight(light);
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
}