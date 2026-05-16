using LoupixDeck.Models;
using LoupixDeck.Services;

namespace LoupixDeck.Controllers;

/// <summary>
/// Lifecycle contract every device controller exposes to the rest of the app.
/// Keeps MainWindowViewModel and PageCommands decoupled from the concrete
/// controller implementation now that we support more than one device family.
/// </summary>
public interface IDeviceController
{
    IPageManager PageManager { get; }
    LoupedeckConfig Config { get; }

    Task Initialize(string port = null, int baudrate = 0);
    void SaveConfig();
}
