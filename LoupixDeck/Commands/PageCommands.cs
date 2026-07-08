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

        loupedeck.AnimateNextRotaryPage();
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

        loupedeck.AnimatePreviousRotaryPage();
        return Task.CompletedTask;
    }
}

[Command("System.GotoPage", "Go to Touch Page by number", "Pages",
    parameterTemplate: "({Page})",
    parameterNames: ["Page"],
    parameterTypes: [typeof(int)])]
public class GotoPageCommand(IDeviceController controller) : IExecutableCommand
{
    public async Task Execute(string[] parameters)
    {
        if (parameters.Length != 1 || !int.TryParse(parameters[0], out var page))
        {
            Console.WriteLine("Usage: System.GotoPage(pageNumber) — 1-based");
            return;
        }
        var index = page - 1;
        var pages = controller.PageManager.TouchButtonPages;
        if (index < 0 || index >= pages.Count)
        {
            Console.WriteLine($"Touch page {page} out of range (1-{pages.Count})");
            return;
        }
        await controller.PageManager.ApplyTouchPage(index);
    }
}

[Command("System.GotoRotaryPage", "Go to Rotary Page by number", "Pages",
    parameterTemplate: "({Page})",
    parameterNames: ["Page"],
    parameterTypes: [typeof(int)])]
public class GotoRotaryPageCommand(IDeviceController controller) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters.Length != 1 || !int.TryParse(parameters[0], out var page))
        {
            Console.WriteLine("Usage: System.GotoRotaryPage(pageNumber) — 1-based");
            return Task.CompletedTask;
        }
        var index = page - 1;
        var pages = controller.PageManager.RotaryButtonPages;
        if (index < 0 || index >= pages.Count)
        {
            Console.WriteLine($"Rotary page {page} out of range (1-{pages.Count})");
            return Task.CompletedTask;
        }
        controller.AnimateGotoRotaryPage(index);
        return Task.CompletedTask;
    }
}