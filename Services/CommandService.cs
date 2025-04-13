using Avalonia.Threading;

namespace LoupixDeck.Services;

public interface ICommandService
{
    void ExecuteCommand(string command);
}

public class CommandService : ICommandService
{
    private readonly ISysCommandService _sysCommandService;
    private readonly ICommandRunner _commandRunner;

    public CommandService(ISysCommandService sysCommandService, ICommandRunner commandRunner)
    {
        _sysCommandService = sysCommandService;
        _commandRunner = commandRunner;
    }

    public void ExecuteCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return;

        var cleanCommand = GetCommandWithoutParameter(command);

        if (_sysCommandService.CheckCommandExists(cleanCommand))
        {
            var parameters = GetCommandParameters(command);
            Dispatcher.UIThread.Post(() => { _sysCommandService.ExecuteCommand(cleanCommand, parameters); });
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