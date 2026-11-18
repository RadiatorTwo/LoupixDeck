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
// The picker lists them in the order they are declared here.

[Command("System.SetLedButtonState", "Set LED Button State", "Button States",
    parameterTemplate: "({Button},{State})",
    parameterNames: ["Button", "State"],
    parameterTypes: [typeof(LedButton), typeof(string)],
    Description = "Switch an LED button to one of its states, by state name or number")]
public sealed class SetLedButtonStateCommand(IDeviceController controller) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters is not { Length: 2 } || !ButtonStateTargets.TryParseName(parameters[0], out LedButton led))
        {
            Console.WriteLine("Usage: System.SetLedButtonState(Button0|...|Button7|All,stateName)");
            return Task.CompletedTask;
        }

        foreach (SimpleButton button in ButtonStateTargets.LedButtons(controller, led, "[SetLedButtonState]"))
            ButtonStateTargets.SetState(button, parameters[1], $"[SetLedButtonState] {button.Id}");
        return Task.CompletedTask;
    }
}

[Command("System.ResetLedButtonStates", "Reset LED Button States", "Button States",
    parameterTemplate: "({Button})",
    parameterNames: ["Button"],
    parameterTypes: [typeof(LedButton)],
    Description = "Put an LED button, or all of them, back to its default state")]
public sealed class ResetLedButtonStatesCommand(IDeviceController controller) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        // No choice made reads as All, the only sensible default for a reset.
        string choice = parameters is { Length: > 0 } ? parameters[0] : null;
        LedButton led = LedButton.All;
        if (!string.IsNullOrWhiteSpace(choice) && !ButtonStateTargets.TryParseName(choice, out led))
        {
            Console.WriteLine("Usage: System.ResetLedButtonStates(Button0|...|Button7|All)");
            return Task.CompletedTask;
        }

        foreach (SimpleButton button in ButtonStateTargets.LedButtons(controller, led, "[ResetLedButtonStates]"))
            ButtonStateTargets.Reset(button);
        return Task.CompletedTask;
    }
}

[Command("System.SetTouchButtonState", "Set Touch Button State", "Button States",
    parameterTemplate: "({Page},{Button},{State})",
    parameterNames: ["Page", "Button", "State"],
    parameterTypes: [typeof(int), typeof(TouchButtonNumber), typeof(string)],
    Description = "Switch a touch button to one of its states, by page number, button number and state name or number")]
public sealed class SetTouchButtonStateCommand(IDeviceController controller) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters is not { Length: 3 } ||
            !int.TryParse(parameters[0], out int page) ||
            !ButtonStateTargets.TryParseName(parameters[1], out TouchButtonNumber number))
        {
            Console.WriteLine("Usage: System.SetTouchButtonState(pageNumber,Button1|...|Button15|All,stateName)");
            return Task.CompletedTask;
        }

        foreach (TouchButton button in ButtonStateTargets.TouchButtons(controller, page, number, "[SetTouchButtonState]"))
            ButtonStateTargets.SetState(button, parameters[2],
                $"[SetTouchButtonState] page {page}, button {button.Index + 1}");
        return Task.CompletedTask;
    }
}

[Command("System.ResetTouchButtonStates", "Reset Touch Button States", "Button States",
    parameterTemplate: "({Page},{Button})",
    parameterNames: ["Page", "Button"],
    parameterTypes: [typeof(int), typeof(TouchButtonNumber)],
    Description = "Put a touch button of a page, or all of them, back to its default state")]
public sealed class ResetTouchButtonStatesCommand(IDeviceController controller) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        if (parameters is not { Length: > 0 } || !int.TryParse(parameters[0], out int page))
        {
            Console.WriteLine("Usage: System.ResetTouchButtonStates(pageNumber,Button1|...|Button15|All)");
            return Task.CompletedTask;
        }

        // No choice made reads as All, as for the LED reset.
        string choice = parameters.Length > 1 ? parameters[1] : null;
        TouchButtonNumber number = TouchButtonNumber.All;
        if (!string.IsNullOrWhiteSpace(choice) && !ButtonStateTargets.TryParseName(choice, out number))
        {
            Console.WriteLine("Usage: System.ResetTouchButtonStates(pageNumber,Button1|...|Button15|All)");
            return Task.CompletedTask;
        }

        foreach (TouchButton button in ButtonStateTargets.TouchButtons(controller, page, number, "[ResetTouchButtonStates]"))
            ButtonStateTargets.Reset(button);
        return Task.CompletedTask;
    }
}

/// <summary>Shared lookup and state switching for the button state commands.</summary>
internal static class ButtonStateTargets
{
    /// <summary>The active profile's LED buttons <paramref name="led"/> stands for; logs a missing one.</summary>
    public static IEnumerable<SimpleButton> LedButtons(IDeviceController controller, LedButton led, string logPrefix)
    {
        SimpleButton[] buttons = controller.Config.SimpleButtons ?? [];
        if (led == LedButton.All)
            return buttons;

        SimpleButton button = buttons.FindById((Constants.ButtonType)led);
        if (button == null)
            Console.WriteLine($"{logPrefix} this device has no {led}");
        return button == null ? [] : [button];
    }

    /// <summary>
    /// The touch buttons <paramref name="number"/> stands for on the 1-based <paramref name="page"/>
    /// of the active workspace; logs a missing page or button.
    /// </summary>
    public static IEnumerable<TouchButton> TouchButtons(IDeviceController controller, int page,
        TouchButtonNumber number, string logPrefix)
    {
        System.Collections.ObjectModel.ObservableCollection<TouchButtonPage> pages = controller.Config.TouchButtonPages;
        if (pages == null || page < 1 || page > pages.Count)
        {
            Console.WriteLine($"{logPrefix} touch page {page} out of range (1-{pages?.Count ?? 0})");
            return [];
        }

        System.Collections.ObjectModel.ObservableCollection<TouchButton> buttons = pages[page - 1].TouchButtons;
        if (buttons == null)
            return [];
        if (number == TouchButtonNumber.All)
            return buttons;

        TouchButton button = buttons.FindByIndex((int)number - 1);
        if (button == null)
            Console.WriteLine($"{logPrefix} page {page} has no {number}");
        return button == null ? [] : [button];
    }

    /// <summary>
    /// Switches <paramref name="button"/> to the state given by <paramref name="stateName"/>: a
    /// state name, or else its 1-based position in the editor's States list.
    /// </summary>
    public static void SetState(StatefulButton button, string stateName, string logContext)
    {
        if (button.HasCommandOwnedStates)
        {
            Console.WriteLine($"{logContext}: states are driven by its command, left unchanged");
            return;
        }

        string name = stateName?.Trim();
        // A name wins, so a state the user named "2" is still found by its name.
        ButtonState target = button.States?.FirstOrDefault(s =>
            string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        if (target == null && int.TryParse(name, out int position) &&
            button.States != null && position >= 1 && position <= button.States.Count)
            target = button.States[position - 1];
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
    /// Parses an enum value by name only. <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/>
    /// would also take a number and read it as the raw value.
    /// </summary>
    public static bool TryParseName<TEnum>(string text, out TEnum value) where TEnum : struct, Enum
    {
        string name = Enum.GetNames<TEnum>().FirstOrDefault(n =>
            string.Equals(n, text?.Trim(), StringComparison.OrdinalIgnoreCase));
        value = name == null ? default : Enum.Parse<TEnum>(name);
        return name != null;
    }
}
