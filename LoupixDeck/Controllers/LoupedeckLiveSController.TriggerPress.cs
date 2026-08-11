using System.Collections.Concurrent;
using LoupixDeck.LoupedeckDevice;
using LoupixDeck.Services.Macros;

namespace LoupixDeck.Controllers;

/// <summary>
/// Tracks which physical controls are currently held down so a macro can wait for its own
/// trigger to be released (#185). A press token is created at the moment a user command is
/// dispatched, published as the ambient <see cref="TriggerPressScope"/> for that dispatch,
/// and completed when the matching BUTTON_UP / TOUCH_END arrives.
///
/// Touch presses are keyed by the firmware's touch id, not by the slot: that is the identity
/// TOUCH_END reports, it keeps two fingers independent, and it survives a finger sliding off
/// the button it started on. The single-slot <c>_activeTouchSlot</c> latch is a different
/// concern (sliding prevention) and stays untouched.
/// </summary>
public partial class LoupedeckLiveSController
{
    private readonly ConcurrentDictionary<Constants.ButtonType, TriggerPress> _buttonPresses = new();
    private readonly ConcurrentDictionary<byte, TriggerPress> _touchPresses = new();

    /// <summary>
    /// Starts tracking a button press. A still-open token for the same button is force-released
    /// first, which covers a BUTTON_UP that never arrived.
    /// </summary>
    private TriggerPress BeginButtonPress(Constants.ButtonType id)
    {
        if (_buttonPresses.TryRemove(id, out TriggerPress previous))
            previous.Release();

        TriggerPress press = new($"Button:{id}");
        _buttonPresses[id] = press;
        RemoveOnRelease(_buttonPresses, id, press);
        return press;
    }

    private void CompleteButtonPress(Constants.ButtonType id)
    {
        if (_buttonPresses.TryRemove(id, out TriggerPress press))
            press.Release();
    }

    /// <summary>
    /// Starts tracking a touch press, or returns the still-open token for this contact.
    ///
    /// Reuse is required, not an optimisation: the device raises TOUCH_START for the whole run of
    /// touch packets, mid-drag samples included (see LoupedeckDevice.OnTouchReceived), so a finger
    /// that wobbles reaches this method repeatedly. Replacing the token there would end the hold
    /// while the finger is still down. A token whose TOUCH_END was lost therefore lives on until
    /// its watchdog fires, which is the safe direction to err in.
    /// </summary>
    private TriggerPress BeginTouchPress(byte touchId)
    {
        if (_touchPresses.TryGetValue(touchId, out TriggerPress existing) && existing.IsHeld)
            return existing;

        TriggerPress press = new($"Touch:{touchId}");
        _touchPresses[touchId] = press;
        RemoveOnRelease(_touchPresses, touchId, press);
        return press;
    }

    private void CompleteTouchPress(byte touchId)
    {
        if (_touchPresses.TryRemove(touchId, out TriggerPress press))
            press.Release();
    }

    /// <summary>
    /// Ends every tracked press. Called only where the release event genuinely cannot arrive any
    /// more — device off / suspend, resume, shutdown — plus a profile or workspace switch, which
    /// discards the context the waiting macro belonged to.
    ///
    /// Deliberately NOT called on a page switch, screensaver start or a plugin display takeover:
    /// tokens are keyed by the physical control and the release is processed before any of those
    /// consume the event, so the real release still lands. Releasing there would cut short a long
    /// hold (the screensaver arms on idle, and a held button produces no further events).
    /// </summary>
    private void ReleaseAllPresses()
    {
        foreach (TriggerPress press in _buttonPresses.Values)
            press.Release();

        foreach (TriggerPress press in _touchPresses.Values)
            press.Release();
    }

    /// <summary>
    /// Runs <paramref name="dispatch"/> with a fresh press token as the ambient trigger press.
    /// The scope is left as soon as the dispatch returns: the Fire* methods only need it long
    /// enough for their Task.Run to capture the ExecutionContext, and the serial-read thread must
    /// not carry the ambient into the next hardware event.
    /// </summary>
    private void DispatchWithPress(Constants.ButtonType id, Action dispatch)
    {
        TriggerPress press = BeginButtonPress(id);
        using IDisposable scope = TriggerPressScope.Enter(press);
        dispatch();
    }

    /// <summary>
    /// Drops the entry once the press ends (including via the watchdog) so a token whose release
    /// event never arrives cannot leak. The key/value overload of TryRemove guarantees a newer
    /// token for the same key is never evicted by an older one's release.
    /// </summary>
    private static void RemoveOnRelease<TKey>(ConcurrentDictionary<TKey, TriggerPress> presses,
        TKey key, TriggerPress press)
    {
        press.Released.ContinueWith(
            _ => presses.TryRemove(new KeyValuePair<TKey, TriggerPress>(key, press)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
