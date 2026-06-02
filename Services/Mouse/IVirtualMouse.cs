using LoupixDeck.Models.Macros;

namespace LoupixDeck.Services.Mouse;

/// <summary>
/// Virtual mouse used by macro mouse steps. Windows: SendInput; Linux: uinput.
/// Kept separate from <see cref="IUInputKeyboard"/> so the keyboard backends
/// (including Interception) stay untouched.
/// </summary>
public interface IVirtualMouse : IDisposable
{
    /// <summary>False when the backend could not be initialized (all calls become no-ops).</summary>
    bool Connected { get; }

    /// <summary>Presses and releases a mouse button.</summary>
    void Click(MouseButton button);

    /// <summary>Presses a mouse button and keeps it held.</summary>
    void ButtonDown(MouseButton button);

    /// <summary>Releases a mouse button previously held by <see cref="ButtonDown"/>.</summary>
    void ButtonUp(MouseButton button);

    /// <summary>Moves the cursor by a relative delta in device units (≈ pixels).</summary>
    void MoveRelative(int dx, int dy);

    /// <summary>
    /// Moves the cursor to an absolute screen position in pixels.
    /// Not supported on Linux (no-op with a log message).
    /// </summary>
    void MoveAbsolute(int x, int y);

    /// <summary>Scrolls the wheel by the given number of detents (positive = up).</summary>
    void Scroll(int amount);
}
