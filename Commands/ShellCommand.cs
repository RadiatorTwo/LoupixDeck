using LoupixDeck.Commands.Base;
using LoupixDeck.Services;

namespace LoupixDeck.Commands;

/// <summary>
/// Runs an arbitrary shell command. The command text is entered freely in the chip and
/// stored verbatim as its own chain segment, so it executes through the unknown-command
/// path in <see cref="Services.CommandService"/> → <see cref="ICommandRunner"/> (cross-platform:
/// <c>cmd.exe /c</c> on Windows, <c>/bin/bash -c</c> on Linux). This registered command exists
/// mainly to surface the entry in the command picker and to allow execution by name.
/// </summary>
[Command(
    "System.Shell",
    "Shell Command",
    "Shell",
    "({Command})",
    ["Command"],
    [typeof(string)],
    Platform = CommandPlatform.All)]
public class ShellCommand(ICommandRunner commandRunner) : IExecutableCommand
{
    public const string CommandName = "System.Shell";

    public Task Execute(string[] parameters)
    {
        if (parameters.Length == 0)
            return Task.CompletedTask;

        // Tolerate by-name calls that contain commas: GetParameters splits the raw
        // string on ',', so rejoin them into the original command line.
        var command = string.Join(", ", parameters);
        if (!string.IsNullOrWhiteSpace(command))
            commandRunner.EnqueueCommand(command);

        return Task.CompletedTask;
    }
}
