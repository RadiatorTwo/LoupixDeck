using LoupixDeck.Commands.Base;
using LoupixDeck.Controllers;

namespace LoupixDeck.Commands;

[Command("System.NextPage","Next Touch Page", "Pages")]
public class PreviousTouchPageCommand(LoupedeckLiveSController loupedeck) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters.Length != 0)
        {
            Console.WriteLine("Invalid Parameter count");
            return Task.CompletedTask;
        }

        loupedeck.PageManager.NextTouchPage();
        return Task.CompletedTask;
    }
}

[Command("System.PreviousPage","Previous Touch Page", "Pages")]
public class NextTouchPageCommand(LoupedeckLiveSController loupedeck) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters.Length != 0)
        {
            Console.WriteLine("Invalid Parameter count");
            return Task.CompletedTask;
        }

        loupedeck.PageManager.PreviousTouchPage();
        return Task.CompletedTask;
    }
}

[Command("System.NextRotaryPage","Next Rotary Page", "Pages")]
public class NextRotaryPageCommand(LoupedeckLiveSController loupedeck) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters.Length != 0)
        {
            Console.WriteLine("Invalid Parameter count");
            return Task.CompletedTask;
        }

        loupedeck.PageManager.NextRotaryPage();
        return Task.CompletedTask;
    }
}

[Command("System.PreviousRotaryPage","Previous Rotary Page", "Pages")]
public class PreviousRotaryPageCommand(LoupedeckLiveSController loupedeck) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters.Length != 0)
        {
            Console.WriteLine("Invalid Parameter count");
            return Task.CompletedTask;
        }

        loupedeck.PageManager.PreviousRotaryPage();
        return Task.CompletedTask;
    }
}