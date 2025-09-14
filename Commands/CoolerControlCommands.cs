using LoupixDeck.Commands.Base;
using LoupixDeck.Services;

namespace LoupixDeck.Commands;

[Command(
    "System.CoolerControlSetMode",
    "Set Mode",
    "Cooler Control",
    "({UID})",
    ["UID"],
    [typeof(string)])]
public class CoolerControlSetModeCommand(ICoolerControlApiController controller) : IExecutableCommand
{
    public async Task Execute(string[] parameters)
    {
        if (parameters.Length != 1)
        {
            Console.WriteLine("Invalid Parametercount");
            return;
        }

        await controller.SetMode(parameters[0]);
    }
}