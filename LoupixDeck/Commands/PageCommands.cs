using LoupixDeck.Commands.Base;
using LoupixDeck.Controllers;
using LoupixDeck.Models;

namespace LoupixDeck.Commands;

// ── Touch pages ───────────────────────────────────────────────────────────────────────

[Command("System.NextPage", "Next Touch Page", "Pages", Description = "Go to the next touch page")]
public class PreviousTouchPageCommand(LoupedeckLiveSController loupedeck) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters.Length != 0)
        {
            Console.WriteLine("Invalid Parameter count");
            return Task.CompletedTask;
        }

        loupedeck.AnimateNextTouchPage();
        return Task.CompletedTask;
    }
}

[Command("System.PreviousPage", "Previous Touch Page", "Pages", Description = "Go to the previous touch page")]
public class NextTouchPageCommand(LoupedeckLiveSController loupedeck) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters.Length != 0)
        {
            Console.WriteLine("Invalid Parameter count");
            return Task.CompletedTask;
        }

        loupedeck.AnimatePreviousTouchPage();
        return Task.CompletedTask;
    }
}

[Command("System.GotoPage", "Go to Touch Page by number", "Pages",
    parameterTemplate: "({Page})",
    parameterNames: ["Page"],
    parameterTypes: [typeof(int)],
    Description = "Jump to a touch page by number")]
public class GotoPageCommand(IDeviceController controller) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters.Length != 1 || !int.TryParse(parameters[0], out var page))
        {
            Console.WriteLine("Usage: System.GotoPage(pageNumber) — 1-based");
            return Task.CompletedTask;
        }
        var index = page - 1;
        var pages = controller.PageManager.TouchButtonPages;
        if (index < 0 || index >= pages.Count)
        {
            Console.WriteLine($"Touch page {page} out of range (1-{pages.Count})");
            return Task.CompletedTask;
        }
        controller.AnimateGotoTouchPage(index);
        return Task.CompletedTask;
    }
}

// ── Rotary pages — global (both columns on side-strip devices) ─────────────────────────

[Command("System.NextRotaryPage", "Next Rotary Page", "Pages", Description = "Go to the next rotary page")]
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

[Command("System.PreviousRotaryPage", "Previous Rotary Page", "Pages", Description = "Go to the previous rotary page")]
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

[Command("System.GotoRotaryPage", "Go to Rotary Page by number", "Pages",
    parameterTemplate: "({Page})",
    parameterNames: ["Page"],
    parameterTypes: [typeof(int)],
    Description = "Jump to a rotary page by number")]
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

// ── Rotary pages — per side (issue #138) ──────────────────────────────────────────────
// Only offered on devices with separate side-display rotary areas (RequiresSideStrips);
// the Loupedeck Live S never sees them. The global commands above already page both
// columns, so there is no separate "both" variant.

[Command("System.NextRotaryPageLeft", "Next Left Rotary Page", "Pages", RequiresSideStrips = true)]
public class NextLeftRotaryPageCommand(IDeviceController controller) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters.Length != 0)
        {
            Console.WriteLine("Invalid Parameter count");
            return Task.CompletedTask;
        }

        controller.AnimateRotaryPageForSide(RotarySide.Left, next: true);
        return Task.CompletedTask;
    }
}

[Command("System.PreviousRotaryPageLeft", "Previous Left Rotary Page", "Pages", RequiresSideStrips = true)]
public class PreviousLeftRotaryPageCommand(IDeviceController controller) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters.Length != 0)
        {
            Console.WriteLine("Invalid Parameter count");
            return Task.CompletedTask;
        }

        controller.AnimateRotaryPageForSide(RotarySide.Left, next: false);
        return Task.CompletedTask;
    }
}

[Command("System.GotoRotaryPageLeft", "Go to Left Rotary Page by number", "Pages",
    parameterTemplate: "({Page})",
    parameterNames: ["Page"],
    parameterTypes: [typeof(int)],
    RequiresSideStrips = true)]
public class GotoLeftRotaryPageCommand(IDeviceController controller) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters.Length != 1 || !int.TryParse(parameters[0], out var page))
        {
            Console.WriteLine("Usage: System.GotoRotaryPageLeft(pageNumber) — 1-based");
            return Task.CompletedTask;
        }
        var index = page - 1;
        var pages = controller.PageManager.GetRotaryPages(RotarySide.Left);
        if (index < 0 || index >= pages.Count)
        {
            Console.WriteLine($"Left rotary page {page} out of range (1-{pages.Count})");
            return Task.CompletedTask;
        }
        controller.AnimateGotoRotaryPageForSide(RotarySide.Left, index);
        return Task.CompletedTask;
    }
}

[Command("System.NextRotaryPageRight", "Next Right Rotary Page", "Pages", RequiresSideStrips = true)]
public class NextRightRotaryPageCommand(IDeviceController controller) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters.Length != 0)
        {
            Console.WriteLine("Invalid Parameter count");
            return Task.CompletedTask;
        }

        controller.AnimateRotaryPageForSide(RotarySide.Right, next: true);
        return Task.CompletedTask;
    }
}

[Command("System.PreviousRotaryPageRight", "Previous Right Rotary Page", "Pages", RequiresSideStrips = true)]
public class PreviousRightRotaryPageCommand(IDeviceController controller) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters.Length != 0)
        {
            Console.WriteLine("Invalid Parameter count");
            return Task.CompletedTask;
        }

        controller.AnimateRotaryPageForSide(RotarySide.Right, next: false);
        return Task.CompletedTask;
    }
}

[Command("System.GotoRotaryPageRight", "Go to Right Rotary Page by number", "Pages",
    parameterTemplate: "({Page})",
    parameterNames: ["Page"],
    parameterTypes: [typeof(int)],
    RequiresSideStrips = true)]
public class GotoRightRotaryPageCommand(IDeviceController controller) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters.Length != 1 || !int.TryParse(parameters[0], out var page))
        {
            Console.WriteLine("Usage: System.GotoRotaryPageRight(pageNumber) — 1-based");
            return Task.CompletedTask;
        }
        var index = page - 1;
        var pages = controller.PageManager.GetRotaryPages(RotarySide.Right);
        if (index < 0 || index >= pages.Count)
        {
            Console.WriteLine($"Right rotary page {page} out of range (1-{pages.Count})");
            return Task.CompletedTask;
        }
        controller.AnimateGotoRotaryPageForSide(RotarySide.Right, index);
        return Task.CompletedTask;
    }
}