using LoupixDeck.Commands.Base;
using LoupixDeck.Models.Macros;
using LoupixDeck.Services;
using LoupixDeck.Services.Mouse;

namespace LoupixDeck.Commands;

[Command(
    "System.MouseScroll",
    "Mouse Scroll",
    "Macros",
    "({Amount})",
    ["Amount"],
    [typeof(int)],
    ["1"],
    Platform = CommandPlatform.All,
    Icon = "\U000F037D", // mdi-mouse
    Description = "Scroll the mouse wheel (positive = up, negative = down)")]
public class MouseScrollCommand(IVirtualMouse mouse) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters.Length != 1)
        {
            Console.WriteLine("Usage: System.MouseScroll(amount)");
            return Task.CompletedTask;
        }

        if (!int.TryParse(parameters[0], out int amount))
        {
            Console.WriteLine($"[MouseScroll] invalid amount '{parameters[0]}'");
            return Task.CompletedTask;
        }

        mouse.Scroll(amount);
        return Task.CompletedTask;
    }
}

[Command(
    "System.MouseClick",
    "Mouse Click",
    "Macros",
    "({Button})",
    ["Button"],
    [typeof(MouseButton)],
    [nameof(MouseButton.Left)],
    Platform = CommandPlatform.All,
    Icon = "\U000F037D", // mdi-mouse
    Description = "Click a mouse button at the current cursor position")]
public class MouseClickCommand(IVirtualMouse mouse) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters.Length != 1)
        {
            Console.WriteLine("Usage: System.MouseClick(Left|Right|Middle|X1|X2)");
            return Task.CompletedTask;
        }

        if (!Enum.TryParse(parameters[0], ignoreCase: true, out MouseButton button))
        {
            Console.WriteLine($"[MouseClick] unknown button '{parameters[0]}'");
            return Task.CompletedTask;
        }

        mouse.Click(button);
        return Task.CompletedTask;
    }
}

/// <summary>
/// A keyboard-plus-mouse chord: holds one or more keyboard keys (usually modifiers) while
/// performing a single mouse action, then releases them again. This covers Ctrl-click or
/// Shift-scroll, which neither a pure key combination nor a pure mouse click can express.
/// </summary>
[Command(
    "System.MouseCombo",
    "Keyboard + Mouse",
    "Macros",
    "({Keys},{Action})",
    ["Keys", "Action"],
    [typeof(string), typeof(MouseComboAction)],
    ["Ctrl", nameof(MouseComboAction.Left)],
    ParameterPickers = [ParameterPicker.Modifiers],
    Platform = CommandPlatform.All,
    Icon = "\U000F037D", // mdi-mouse
    Description = "Hold keyboard keys while clicking or scrolling the mouse")]
public class MouseComboCommand(IUInputKeyboard keyboard, IVirtualMouse mouse) : IExecutableCommand
{
    /// <summary>
    /// How long the held keys settle before the mouse acts. Without the gap the mouse event can
    /// reach the target application before the modifier is seen as down, and it behaves like a
    /// plain click.
    /// </summary>
    private const int ModifierSettleMs = 15;

    public Task Execute(string[] parameters)
    {
        // An empty parameter is dropped by the parser, so a chord without keys arrives as the
        // action alone — that stays valid and simply performs the mouse action on its own.
        if (parameters.Length is not (1 or 2))
        {
            Console.WriteLine("Usage: System.MouseCombo(keys,action), e.g. System.MouseCombo(Ctrl+Shift,Left)");
            return Task.CompletedTask;
        }

        string actionText = parameters[^1];
        if (!Enum.TryParse(actionText, ignoreCase: true, out MouseComboAction action))
        {
            Console.WriteLine($"[MouseCombo] unknown mouse action '{actionText}'");
            return Task.CompletedTask;
        }

        string[] keys = parameters.Length == 2
            ? parameters[0].Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];

        // Hold the keyboard keys, act on the mouse while they are down, release in reverse order.
        foreach (string key in keys)
            keyboard.KeyDown(key);

        try
        {
            if (keys.Length > 0)
                Thread.Sleep(ModifierSettleMs);

            DoMouse(action);
        }
        finally
        {
            for (int i = keys.Length - 1; i >= 0; i--)
                keyboard.KeyUp(keys[i]);
        }

        return Task.CompletedTask;
    }

    private void DoMouse(MouseComboAction action)
    {
        switch (action)
        {
            case MouseComboAction.ScrollUp:
                mouse.Scroll(1);
                break;
            case MouseComboAction.ScrollDown:
                mouse.Scroll(-1);
                break;
            case MouseComboAction.Right:
                mouse.Click(MouseButton.Right);
                break;
            case MouseComboAction.Middle:
                mouse.Click(MouseButton.Middle);
                break;
            case MouseComboAction.X1:
                mouse.Click(MouseButton.X1);
                break;
            case MouseComboAction.X2:
                mouse.Click(MouseButton.X2);
                break;
            default:
                mouse.Click(MouseButton.Left);
                break;
        }
    }
}
