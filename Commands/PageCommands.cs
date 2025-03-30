using LoupixDeck.Commands.Base;
using LoupixDeck.Models;

namespace LoupixDeck.Commands;

[Command("System.NextPage")]
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

[Command("System.PreviousPage")]
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

[Command("System.PreviousPage")]
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

[Command("System.PreviousPage")]
public class NextRotaryPageCommand(LoupedeckLiveS loupedeck) : IExecutableCommand
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