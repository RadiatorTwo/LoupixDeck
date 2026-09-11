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
    Platform = CommandPlatform.All,
    Icon = "\U000F040A", // mdi-play
    Description = "Run a basic macro")]
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
    ParameterPickers = [ParameterPicker.KeyCombination],
    Platform = CommandPlatform.All,
    Icon = "\U000F030C", // mdi-keyboard
    Description = "Execute key sequences")]
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
    "System.KeySequence",
    "Key Sequence",
    "Macros",
    "({Steps})",
    ["Steps"],
    [typeof(string)],
    ParameterPickers = [ParameterPicker.KeySequence],
    Platform = CommandPlatform.All,
    Icon = "\U000F030C", // mdi-keyboard
    Description = "Play several key presses one after another")]
public class KeySequenceCommand(IUInputKeyboard uInputKeyboard) : IExecutableCommand
{
    /// <summary>
    /// Gap between two steps. Without it a repeated key arrives as one long press instead of two
    /// separate ones, and the target application sees a single keystroke.
    /// </summary>
    private const int StepGapMs = 40;

    public Task Execute(string[] parameters)
    {
        // A sequence is a comma-separated list of ordinary combos, e.g. "X, Alt, Alt, Ctrl+C".
        // The command parser already splits on ',', so each step arrives as its own parameter.
        // The steps run one after another (unlike System.KeyCombination, which holds every key at
        // once), so the same key may appear several times — that is the point of a sequence.
        if (parameters.Length == 0)
        {
            Console.WriteLine("Usage: System.KeySequence(step[,step…]), e.g. System.KeySequence(X,Alt,Ctrl+C)");
            return Task.CompletedTask;
        }

        for (int i = 0; i < parameters.Length; i++)
        {
            string[] keys = parameters[i]
                .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (keys.Length == 0)
                continue;

            uInputKeyboard.SendKeyCombination(keys);

            if (i < parameters.Length - 1)
                Thread.Sleep(StepGapMs);
        }

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

[Command(
    "System.StopMacros",
    "Stop Macros",
    "User Macros",
    Platform = CommandPlatform.All,
    Icon = "\U000F04DB", // mdi-stop
    Description = "Cancel all running macros")]
public class StopMacrosCommand(MacroRunner macroRunner) : IExecutableCommand
{
    public Task Execute(string[] parameters)
    {
        // Cancels every running macro — the user-assignable Stop trigger.
        macroRunner.CancelAll();
        return Task.CompletedTask;
    }
}