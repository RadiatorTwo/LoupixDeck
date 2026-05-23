using System.Text.RegularExpressions;
using LoupixDeck.PluginSdk;
using LoupixDeck.Services.Commands;

namespace LoupixDeck.Services;

public interface ICommandService
{
    /// <summary>
    /// Executes a command string. <paramref name="target"/> is the button type
    /// that triggered the call (or <see cref="ButtonTargets.None"/> when the
    /// origin is not a button — CLI, plugin-to-plugin chaining, etc.).
    /// Chained commands joined by <c>&amp;&amp;</c> all inherit this target.
    /// </summary>
    Task ExecuteCommand(string command, ButtonTargets target);
}

public class CommandService : ICommandService
{
    private readonly ICommandRegistry _commandRegistry;
    private readonly ICommandRunner _commandRunner;

    public CommandService(ICommandRegistry commandRegistry, ICommandRunner commandRunner)
    {
        _commandRegistry = commandRegistry;
        _commandRunner = commandRunner;
    }

    // Splits on "&&" with any amount of surrounding whitespace, so both
    // "a && b" and "a&&b" work the same. Static so the compiled regex
    // instance is reused across calls.
    private static readonly Regex ChainSplitter = new(@"\s*&&\s*", RegexOptions.Compiled);

    public async Task ExecuteCommand(string command, ButtonTargets target)
    {
        if (string.IsNullOrWhiteSpace(command))
            return;

        // Per-page Pre/Post wraps and inline chains in a single button command
        // both run sequentially. Each part is dispatched as either a System or
        // shell command exactly like before. Note: this changes shell semantics
        // — we no longer rely on the shell's own && short-circuit, the second
        // part runs even if the first failed. Acceptable for the desk-control
        // commands this app targets.
        foreach (var part in ChainSplitter.Split(command))
        {
            if (string.IsNullOrWhiteSpace(part)) continue;
            await ExecuteSingle(part.Trim(), target);
        }
    }

    private async Task ExecuteSingle(string command, ButtonTargets target)
    {
        if (string.IsNullOrWhiteSpace(command)) return;

        var cleanCommand = GetCommandWithoutParameter(command);

        if (_commandRegistry.Contains(cleanCommand))
        {
            var parameters = GetCommandParameters(command);
            await _commandRegistry.Execute(cleanCommand, parameters, target);
        }
        else
        {
            _commandRunner.EnqueueCommand(command);
        }
    }

    private string[] GetCommandParameters(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return Array.Empty<string>();
        }

        var start = command.IndexOf('(');
        var end = command.IndexOf(')');
        if (start == -1 || end == -1 || end <= start)
        {
            return Array.Empty<string>();
        }

        var parameterString = command.Substring(start + 1, end - start - 1);
        return parameterString.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private string GetCommandWithoutParameter(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return string.Empty;
        }

        var end = command.IndexOf('(');
        if (end == -1)
            return command;

        return command.Substring(0, end);
    }
}