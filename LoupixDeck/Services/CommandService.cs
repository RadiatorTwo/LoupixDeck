using LoupixDeck.PluginSdk;
using LoupixDeck.Services.Commands;
using LoupixDeck.Utils;

namespace LoupixDeck.Services;

public interface ICommandService
{
    /// <summary>
    /// Executes a command string. <paramref name="target"/> is the button type
    /// that triggered the call (or <see cref="ButtonTargets.None"/> when the
    /// origin is not a button — CLI, plugin-to-plugin chaining, etc.).
    /// Chained commands joined by <c>&amp;&amp;</c> all inherit this target.
    /// <paramref name="sourceIndex"/> identifies the originating control
    /// (rotary index, touch slot) when the target is an indexed source.
    /// </summary>
    Task ExecuteCommand(string command, ButtonTargets target, int? sourceIndex = null);
}

public class CommandService : ICommandService
{
    private readonly ICommandRegistry _commandRegistry;
    private readonly ICommandRunner _commandRunner;
    private readonly IServiceProvider _deviceProvider;
    private readonly IDeviceRouter _router;

    public CommandService(ICommandRegistry commandRegistry, ICommandRunner commandRunner,
        IServiceProvider deviceProvider, IDeviceRouter router)
    {
        _commandRegistry = commandRegistry;
        _commandRunner = commandRunner;
        _deviceProvider = deviceProvider;
        _router = router;
    }

    public async Task ExecuteCommand(string command, ButtonTargets target, int? sourceIndex = null)
    {
        if (string.IsNullOrWhiteSpace(command))
            return;

        // Mark this device as the ambient target for the whole dispatch, so any
        // plugin host call made while a command runs (incl. nested/chained) reaches
        // THIS device's services (issue #116 phase 2). Flows across awaits.
        using var _routerScope = _router.Enter(_deviceProvider);

        // Per-page Pre/Post wraps and inline chains in a single button command
        // both run sequentially. Each part is dispatched as either a System or
        // shell command exactly like before. Note: this changes shell semantics
        // — we no longer rely on the shell's own && short-circuit, the second
        // part runs even if the first failed. Acceptable for the desk-control
        // commands this app targets. Splitting/dissecting is delegated to
        // CommandStringParser so the command editor stays in lockstep.
        foreach (var part in CommandStringParser.SplitChain(command))
        {
            await ExecuteSingle(part, target, sourceIndex);
        }
    }

    private async Task ExecuteSingle(string command, ButtonTargets target, int? sourceIndex)
    {
        if (string.IsNullOrWhiteSpace(command)) return;

        var cleanCommand = CommandStringParser.GetName(command);

        if (_commandRegistry.Contains(cleanCommand))
        {
            var parameters = CommandStringParser.GetParameters(command);
            await _commandRegistry.Execute(cleanCommand, parameters, target, sourceIndex);
        }
        else
        {
            _commandRunner.EnqueueCommand(command);
        }
    }
}