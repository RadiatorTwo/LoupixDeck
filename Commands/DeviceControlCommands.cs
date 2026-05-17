using Avalonia.Threading;
using LoupixDeck.Commands.Base;
using LoupixDeck.Controllers;
using LoupixDeck.Services;
using LoupixDeck.Utils;

namespace LoupixDeck.Commands;

[Command("System.DeviceOff", "Device OFF (blank display + LEDs)", "Device Control")]
public class DeviceOffCommand(IDeviceController controller) : IExecutableCommand
{
    public Task Execute(string[] parameters) => controller.ClearDeviceState();
}

[Command("System.DeviceOn", "Device ON (restore from config)", "Device Control")]
public class DeviceOnCommand(IDeviceController controller) : IExecutableCommand
{
    public Task Execute(string[] parameters) => controller.RestoreDeviceState();
}

[Command("System.DeviceToggle", "Device Toggle ON/OFF", "Device Control")]
public class DeviceToggleCommand(IDeviceController controller) : IExecutableCommand
{
    public Task Execute(string[] parameters) => controller.ToggleDeviceState();
}

[Command("System.DeviceWakeup", "Device Wakeup (reconnect serial + ON)", "Device Control")]
public class DeviceWakeupCommand(IDeviceController controller, IDeviceService deviceService) : IExecutableCommand
{
    public async Task Execute(string[] parameters)
    {
        try
        {
            deviceService.ReconnectDevice();
            await Task.Delay(500);
            await controller.RestoreDeviceState();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Device wakeup failed: {ex.Message}");
        }
    }
}

[Command("System.ToggleWindow", "Toggle Main Window visibility", "Device Control")]
public class ToggleWindowCommand : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        // Window manipulation must happen on the UI thread.
        Dispatcher.UIThread.Post(() =>
        {
            if (WindowHelper.GetMainWindow() is Views.MainWindow mw)
                mw.ToggleVisibility();
        });
        return Task.CompletedTask;
    }
}
