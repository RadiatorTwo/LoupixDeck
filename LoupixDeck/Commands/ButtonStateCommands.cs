using Avalonia.Threading;
using LoupixDeck.Commands.Base;
using LoupixDeck.Controllers;
using LoupixDeck.LoupedeckDevice;
using LoupixDeck.Models;
using LoupixDeck.Models.Extensions;

namespace LoupixDeck.Commands;

// Commands that switch the state of another button (#651), e.g. to light the LED of the page
// that is shown. They act on the active profile's LED buttons and the active workspace's touch
// pages. Buttons whose states a command owns are skipped: their plugin drives them.

[Command("System.SetLedButtonState", "Set LED Button State", "Button States",
    parameterTemplate: "({Button},{State})",
    parameterNames: ["Button", "State"],
    parameterTypes: [typeof(LedButton), typeof(string)],
    Description = "Switch an LED button to one of its states, by state name")]
public sealed class SetLedButtonStateCommand(IDeviceController controller) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters is not { Length: 2 } || !ButtonStateTargets.TryParseLed(parameters[0], out LedButton led))
        {
            Console.WriteLine("Usage: System.SetLedButtonState(Button1|...|Button8|Home|...,stateName)");
            return Task.CompletedTask;
        }

        SimpleButton button = controller.Config.SimpleButtons?.FindById((Constants.ButtonType)led);
        if (button == null)
        {
            Console.WriteLine($"[SetLedButtonState] this device has no {led}");
            return Task.CompletedTask;
        }

        ButtonStateTargets.SetState(button, parameters[1], $"[SetLedButtonState] {led}");
        return Task.CompletedTask;
    }
}

[Command("System.SetTouchButtonState", "Set Touch Button State", "Button States",
    parameterTemplate: "({Page},{Button},{State})",
    parameterNames: ["Page", "Button", "State"],
    parameterTypes: [typeof(int), typeof(int), typeof(string)],
    Description = "Switch a touch button to one of its states, by page number, button number and state name")]
public sealed class SetTouchButtonStateCommand(IDeviceController controller) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters is not { Length: 3 } ||
            !int.TryParse(parameters[0], out int page) ||
            !int.TryParse(parameters[1], out int number))
        {
            Console.WriteLine("Usage: System.SetTouchButtonState(pageNumber,buttonNumber,stateName) — both 1-based");
            return Task.CompletedTask;
        }

        TouchButtonPage touchPage = ButtonStateTargets.TouchPage(controller, page, "[SetTouchButtonState]");
        if (touchPage == null) return Task.CompletedTask;

        TouchButton button = touchPage.TouchButtons?.FindByIndex(number - 1);
        if (button == null)
        {
            Console.WriteLine($"[SetTouchButtonState] page {page} has no button {number}");
            return Task.CompletedTask;
        }

        ButtonStateTargets.SetState(button, parameters[2], $"[SetTouchButtonState] page {page}, button {number}");
        return Task.CompletedTask;
    }
}

[Command("System.ResetLedButtonStates", "Reset LED Button States", "Button States",
    parameterTemplate: "({Buttons})",
    parameterNames: ["Buttons"],
    parameterTypes: [typeof(string)],
    Description = "Put LED buttons back to their default state. Buttons: e.g. 1-8 or 1 3 Home; empty means all")]
public sealed class ResetLedButtonStatesCommand(IDeviceController controller) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        SimpleButton[] buttons = controller.Config.SimpleButtons ?? [];
        string list = parameters is { Length: > 0 } ? parameters[0] : null;

        if (string.IsNullOrWhiteSpace(list))
        {
            foreach (SimpleButton button in buttons)
                ButtonStateTargets.Reset(button);
            return Task.CompletedTask;
        }

        foreach (string token in ButtonStateTargets.Tokens(list))
        {
            // A number is a round button; anything else is a name such as Home or Button3.
            if (!ButtonStateTargets.TryParseRange(token, out int from, out int to))
            {
                if (ButtonStateTargets.TryParseLed(token, out LedButton named))
                    ButtonStateTargets.Reset(buttons.FindById((Constants.ButtonType)named));
                else
                    Console.WriteLine($"[ResetLedButtonStates] unknown button '{token}'");
                continue;
            }

            for (int number = from; number <= to; number++)
            {
                if (number is < 1 or > 8) continue;
                ButtonStateTargets.Reset(buttons.FindById(Constants.ButtonType.BUTTON0 + (number - 1)));
            }
        }

        return Task.CompletedTask;
    }
}

[Command("System.ResetTouchButtonStates", "Reset Touch Button States", "Button States",
    parameterTemplate: "({Page},{Buttons})",
    parameterNames: ["Page", "Buttons"],
    parameterTypes: [typeof(int), typeof(string)],
    Description = "Put touch buttons of a page back to their default state. Buttons: e.g. 1-4 or 1 3 5; empty means all")]
public sealed class ResetTouchButtonStatesCommand(IDeviceController controller) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters is not { Length: > 0 } || !int.TryParse(parameters[0], out int page))
        {
            Console.WriteLine("Usage: System.ResetTouchButtonStates(pageNumber,[buttons]) — e.g. (2,1-4)");
            return Task.CompletedTask;
        }

        TouchButtonPage touchPage = ButtonStateTargets.TouchPage(controller, page, "[ResetTouchButtonStates]");
        if (touchPage?.TouchButtons == null) return Task.CompletedTask;

        string list = parameters.Length > 1 ? parameters[1] : null;
        if (string.IsNullOrWhiteSpace(list))
        {
            foreach (TouchButton button in touchPage.TouchButtons)
                ButtonStateTargets.Reset(button);
            return Task.CompletedTask;
        }

        foreach (string token in ButtonStateTargets.Tokens(list))
        {
            if (!ButtonStateTargets.TryParseRange(token, out int from, out int to))
            {
                Console.WriteLine($"[ResetTouchButtonStates] unknown button '{token}'");
                continue;
            }

            for (int number = from; number <= to; number++)
                ButtonStateTargets.Reset(touchPage.TouchButtons.FindByIndex(number - 1));
        }

        return Task.CompletedTask;
    }
}

/// <summary>Shared lookup and state switching for the button state commands.</summary>
internal static class ButtonStateTargets
{
    /// <summary>The touch page with the 1-based <paramref name="page"/> number, or null (logged).</summary>
    public static TouchButtonPage TouchPage(IDeviceController controller, int page, string logPrefix)
    {
        System.Collections.ObjectModel.ObservableCollection<TouchButtonPage> pages = controller.Config.TouchButtonPages;
        if (pages == null || page < 1 || page > pages.Count)
        {
            Console.WriteLine($"{logPrefix} touch page {page} out of range (1-{pages?.Count ?? 0})");
            return null;
        }
        return pages[page - 1];
    }

    /// <summary>Switches <paramref name="button"/> to the state named <paramref name="stateName"/>.</summary>
    public static void SetState(StatefulButton button, string stateName, string logContext)
    {
        if (button.HasCommandOwnedStates)
        {
            Console.WriteLine($"{logContext}: states are driven by its command, left unchanged");
            return;
        }

        string name = stateName?.Trim();
        ButtonState target = button.States?.FirstOrDefault(s =>
            string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        if (target == null)
        {
            Console.WriteLine($"{logContext} has no state named '{name}'");
            return;
        }

        Guid id = target.Id;
        // Apply on the UI thread; the button's refresh hook repaints it when visible. Posts run in
        // order, so a reset earlier in the same chain lands before this.
        Dispatcher.UIThread.Post(() => button.SetActiveState(id));
    }

    /// <summary>Puts <paramref name="button"/> back to its default state; null is ignored.</summary>
    public static void Reset(StatefulButton button)
    {
        if (button == null || button.HasCommandOwnedStates) return;
        Dispatcher.UIThread.Post(button.ResetToDefaultState);
    }

    /// <summary>
    /// Parses an LED button by name only. <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/>
    /// would also take a number and read it as the raw button code.
    /// </summary>
    public static bool TryParseLed(string text, out LedButton led)
    {
        string name = Enum.GetNames<LedButton>().FirstOrDefault(n =>
            string.Equals(n, text?.Trim(), StringComparison.OrdinalIgnoreCase));
        led = name == null ? default : Enum.Parse<LedButton>(name);
        return name != null;
    }

    /// <summary>
    /// Splits a button list. Commas separate command parameters, so the list uses spaces or
    /// semicolons instead.
    /// </summary>
    public static string[] Tokens(string list)
        => list.Split([' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Parses "3" or "1-8" into an inclusive range.</summary>
    public static bool TryParseRange(string token, out int from, out int to)
    {
        string[] bounds = token.Split('-', 2, StringSplitOptions.TrimEntries);
        if (int.TryParse(bounds[0], out from))
        {
            if (bounds.Length == 1)
            {
                to = from;
                return true;
            }
            if (int.TryParse(bounds[1], out to) && to >= from)
                return true;
        }

        from = to = 0;
        return false;
    }
}
