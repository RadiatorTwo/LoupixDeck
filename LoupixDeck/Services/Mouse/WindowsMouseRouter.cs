using LoupixDeck.Models;
using LoupixDeck.Models.Macros;

namespace LoupixDeck.Services.Mouse;

/// <summary>
/// Windows <see cref="IVirtualMouse"/> that routes each call to one of two backends:
/// the Interception kernel driver when the user enabled it and the driver is available,
/// otherwise the SendInput backend. The decision is made per call from the live config,
/// so toggling Interception in the settings takes effect immediately (no app restart).
/// Mirrors <see cref="Services.WindowsKeyboardRouter"/>.
/// </summary>
public class WindowsMouseRouter(
    WindowsVirtualMouse sendInput,
    InterceptionMouse interception,
    LoupedeckConfig config) : IVirtualMouse
{
    public bool Connected => Active.Connected;

    // InterceptionEnabled == null means "auto" → use Interception whenever the driver is
    // present; false means the user explicitly turned it off.
    private IVirtualMouse Active =>
        (config.InterceptionEnabled ?? true) && interception.IsDriverAvailable()
            ? interception
            : sendInput;

    public void Click(MouseButton button) => Active.Click(button);

    public void ButtonDown(MouseButton button) => Active.ButtonDown(button);

    public void ButtonUp(MouseButton button) => Active.ButtonUp(button);

    public void MoveRelative(int dx, int dy) => Active.MoveRelative(dx, dy);

    public void MoveAbsolute(int x, int y) => Active.MoveAbsolute(x, y);

    public void Scroll(int amount) => Active.Scroll(amount);

    public void Dispose()
    {
        sendInput.Dispose();
        interception.Dispose();
    }
}
