using LoupixDeck.Commands.Base;
using LoupixDeck.Services;

namespace LoupixDeck.Commands;

/// <summary>
/// Switches to the previous or the next Windows virtual desktop. Both are the system hotkeys
/// Win+Ctrl+Left / Win+Ctrl+Right, offered as their own commands so they can be bound to a button
/// or a dial gesture without knowing the shortcut. Windows only: Linux desktops have no shared
/// shortcut for this, so a Linux user binds their own key combination instead.
/// </summary>
[Command(
    "System.PreviousDesktop",
    "Previous Desktop",
    "Macros",
    Platform = CommandPlatform.Windows,
    Icon = "\U000F004D", // mdi-arrow-left
    Description = "Switch to the virtual desktop on the left")]
public class PreviousDesktopCommand(IUInputKeyboard uInputKeyboard) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        uInputKeyboard.SendKeyCombination(["Win", "Ctrl", "Left"]);
        return Task.CompletedTask;
    }
}

[Command(
    "System.NextDesktop",
    "Next Desktop",
    "Macros",
    Platform = CommandPlatform.Windows,
    Icon = "\U000F0054", // mdi-arrow-right
    Description = "Switch to the virtual desktop on the right")]
public class NextDesktopCommand(IUInputKeyboard uInputKeyboard) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        uInputKeyboard.SendKeyCombination(["Win", "Ctrl", "Right"]);
        return Task.CompletedTask;
    }
}
