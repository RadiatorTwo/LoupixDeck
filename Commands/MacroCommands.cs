using LoupixDeck.Commands.Base;
using LoupixDeck.Services;
using LoupixDeck.Services.Macros;

namespace LoupixDeck.Commands;

[Command(
    "System.SimpleMacro",
    "Simple Macro",
    "Macros",
    "({Text})",
    ["Text"],
    [typeof(string)],
    Platform = CommandPlatform.All)]
public class SimpleMacroCommand(IUInputKeyboard uInputKeyboard) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters.Length != 1)
        {
            Console.WriteLine("Invalid Parametercount");
            return Task.CompletedTask;
        }

        uInputKeyboard.SendText(parameters[0]);
        return Task.CompletedTask;
    }
}

[Command(
    "System.KeyCombination",
    "Key Combination",
    "Macros",
    "({Keys})",
    ["Keys"],
    [typeof(string)],
    Platform = CommandPlatform.All)]
public class KeyCombinationCommand(IUInputKeyboard uInputKeyboard) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters.Length != 1)
        {
            Console.WriteLine("Invalid Parametercount");
            return Task.CompletedTask;
        }

        // e.g. "Ctrl+C" or "Ctrl + Shift + Esc"
        var keys = parameters[0]
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (keys.Length == 0)
            return Task.CompletedTask;

        uInputKeyboard.SendKeyCombination(keys);
        return Task.CompletedTask;
    }
}

[Command(
    "System.Macro",
    "Macro",
    "User Macros",
    "({Name})",
    ["Name"],
    [typeof(string)],
    Platform = CommandPlatform.All,
    // Hidden from the generic group listing — UserMacroMenuContributor emits one
    // menu entry per defined macro instead of a single "Macro" leaf.
    Hidden = true)]
public class MacroCommand(IMacroManager macroManager, MacroRunner macroRunner) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters.Length != 1)
        {
            Console.WriteLine("Invalid Parametercount");
            return Task.CompletedTask;
        }

        var macro = macroManager.Get(parameters[0]);
        if (macro == null)
        {
            Console.Error.WriteLine($"[MacroCommand] Macro '{parameters[0]}' not found.");
            return Task.CompletedTask;
        }

        return macroRunner.Run(macro);
    }
}