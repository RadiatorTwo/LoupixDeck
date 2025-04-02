using LoupixDeck.Commands.Base;
using LoupixDeck.Models;

namespace LoupixDeck.Commands;

[Command("System.NextPage","Next Touch Page", "Pages")]
public class PreviousTouchPageCommand(LoupedeckLiveS loupedeck) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters.Length != 0)
        {
            Console.WriteLine("Invalid Parametercount");
            return Task.CompletedTask;
        }

        loupedeck.NextTouchPage();
        return Task.CompletedTask;
    }
}

[Command("System.PreviousPage","Previous Touch Page", "Pages")]
public class NextTouchPageCommand(LoupedeckLiveS loupedeck) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters.Length != 0)
        {
            Console.WriteLine("Invalid Parametercount");
            return Task.CompletedTask;
        }

        loupedeck.PreviousTouchPage();
        return Task.CompletedTask;
    }
}

[Command("System.NextRotaryPage","Next Rotary Page", "Pages")]
public class NextRotaryPageCommand(LoupedeckLiveS loupedeck) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters.Length != 0)
        {
            Console.WriteLine("Invalid Parametercount");
            return Task.CompletedTask;
        }

        loupedeck.NextRotaryPage();
        return Task.CompletedTask;
    }
}

[Command("System.PreviousRotaryPage","Previous Rotary Page", "Pages")]
public class PreviousRotaryPageCommand(LoupedeckLiveS loupedeck) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters.Length != 0)
        {
            Console.WriteLine("Invalid Parametercount");
            return Task.CompletedTask;
        }

        loupedeck.PreviousRotaryPage();
        return Task.CompletedTask;
    }
}