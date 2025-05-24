using LoupixDeck.Commands.Base;
using LoupixDeck.Services;

namespace LoupixDeck.Commands;

[Command(
    "System.SimpleMacro",
    "Simple Macro",
    "Macros",
    "({Text})",
    ["Text"],
    [typeof(string)])]
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