#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using LoupixDeck.Models.Macros;
using LoupixDeck.Native;

namespace LoupixDeck.Services.Mouse;

/// <summary>
/// Linux implementation backed by a uinput virtual mouse device (relative axes +
/// buttons + wheel). Same P/Invoke pattern as <see cref="UInputKeyboard"/>.
/// Absolute positioning is not supported (would require an EV_ABS device) — see
/// <see cref="MoveAbsolute"/>.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class UInputMouse : IVirtualMouse
{
    private const ushort BTN_LEFT = 0x110;
    private const ushort BTN_RIGHT = 0x111;
    private const ushort BTN_MIDDLE = 0x112;

    private UInputFile? uinputfile;
    private bool _disposed;

    [MemberNotNullWhen(true, nameof(uinputfile))]
    public bool Connected => uinputfile?.IsInvalid is false;

    public UInputMouse()
    {
        try
        {
            uinputfile = UInputFile.CreateMouse();
            uinputfile.Connect(ctx => {
                // Buttons
                ctx.SetupKeys()
                    .SetKeyBit(BTN_LEFT)
                    .SetKeyBit(BTN_RIGHT)
                    .SetKeyBit(BTN_MIDDLE);

                // Relative axes + wheel
                ctx.SetupRelatives()
                    .SetRelXBit()
                    .SetRelYBit()
                    .SetRelWheelBit();
            });
        }
        catch (Exception)
        {
            // Same policy as UInputKeyboard: no exception, just report unavailable.
            uinputfile = null;
        }
    }

    public void Click(MouseButton button)
    {
        if (!Connected) return;

        uinputfile.TapKey(ButtonCode(button));
    }

    public void ButtonDown(MouseButton button)
    {
        if (!Connected) return;

        uinputfile.PressKey(ButtonCode(button));
    }

    public void ButtonUp(MouseButton button)
    {
        if (!Connected) return;

        uinputfile.ReleaseKey(ButtonCode(button));
    }

    public void MoveRelative(int dx, int dy)
    {
        if (!Connected) return;

        uinputfile.SendMouseMoveRelative(dx, dy);
    }

    public void MoveAbsolute(int x, int y)
    {
        // Would require an EV_ABS uinput device with ABS_X/ABS_Y absinfo — out of scope for v1.
        Console.Error.WriteLine("[UInputMouse] MoveAbsolute is not supported on Linux.");
    }

    public void Scroll(int amount)
    {
        if (!Connected) return;
        uinputfile.SendMouseScroll(amount);
    }

    public void Dispose()
    {
        if (_disposed) return;
        uinputfile?.Close();
        uinputfile = null;

        _disposed = true;
    }

    private static ushort ButtonCode(MouseButton button) => button switch
    {
        MouseButton.Right => BTN_RIGHT,
        MouseButton.Middle => BTN_MIDDLE,
        _ => BTN_LEFT
    };
}
