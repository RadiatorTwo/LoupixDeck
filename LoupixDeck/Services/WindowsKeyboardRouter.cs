using LoupixDeck.Models;

namespace LoupixDeck.Services;

/// <summary>
/// Windows <see cref="IUInputKeyboard"/> that routes each call to one of two backends:
/// the Interception kernel driver when the user enabled it and the driver is available,
/// otherwise the SendInput backend. The decision is made per call from the live config,
/// so toggling Interception in the settings takes effect immediately (no app restart).
/// </summary>
public class WindowsKeyboardRouter(
    WindowsUInputKeyboard sendInput,
    InterceptionKeyboard interception,
    LoupedeckConfig config) : IUInputKeyboard
{
    public bool Connected { get; set; } = true;

    // InterceptionEnabled == null means "auto" → use Interception whenever the driver is
    // present; false means the user explicitly turned it off.
    private IUInputKeyboard Active =>
        (config.InterceptionEnabled ?? true) && interception.IsDriverAvailable()
            ? interception
            : sendInput;

    public void SendKey(ushort keyCode) => Active.SendKey(keyCode);

    public void SendText(string text) => Active.SendText(text);

    public void SendKeyCombination(IReadOnlyList<string> keyNames) => Active.SendKeyCombination(keyNames);

    public void KeyDown(string keyName) => Active.KeyDown(keyName);

    public void KeyUp(string keyName) => Active.KeyUp(keyName);

    public void Dispose()
    {
        sendInput.Dispose();
        interception.Dispose();
    }
}
