using LoupixDeck.PluginSdk;
using LoupixDeck.Registry;
using LoupixDeck.Services.Commands;
using LoupixDeck.Services.Companion;
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
    /// <paramref name="ticks"/> carries the rotary delta for a turn (negative for a
    /// left turn); 0 means "not a turn" (a knob press, a button, the CLI). It only
    /// matters for an adjustment command on <see cref="ButtonTargets.RotaryEncoder"/>:
    /// a turn runs its ApplyAdjustment, a press its ApplyReset. Every other command
    /// ignores it and runs exactly as before.
    /// </summary>
    Task ExecuteCommand(string command, ButtonTargets target, int? sourceIndex = null, int ticks = 0);

    /// <summary>
    /// The value an adjustment command wants shown on its dial — scale position plus display
    /// text — or null when <paramref name="command"/> is empty, not registered, not an
    /// adjustment command, or the plugin supplies no value. Called from the side-strip render
    /// path and by plugins that render a dial themselves, so it must stay cheap.
    /// </summary>
    AdjustmentValue? GetAdjustmentValue(string command, int? sourceIndex = null);

    /// <summary>
    /// True when the command string resolves to a registered adjustment command, i.e.
    /// the rotary path drives it through ApplyAdjustment/ApplyReset. Lets the device
    /// controller decide whether a dial needs its indicator repainted after a turn.
    /// </summary>
    bool IsAdjustmentCommand(string command);
}

public class CommandService : ICommandService
{
    private readonly ICommandRegistry _commandRegistry;
    private readonly ICommandRunner _commandRunner;
    private readonly IServiceProvider _deviceProvider;
    private readonly IDeviceRouter _router;

    private readonly ICompanionCoordinator _companions;
    private readonly ResolvedDevice _device;

    public CommandService(ICommandRegistry commandRegistry, ICommandRunner commandRunner,
        IServiceProvider deviceProvider, IDeviceRouter router, ICompanionCoordinator companions, ResolvedDevice device)
    {
        _commandRegistry = commandRegistry;
        _commandRunner = commandRunner;
        _deviceProvider = deviceProvider;
        _router = router;
        _companions = companions;
        _device = device;
    }

    public async Task ExecuteCommand(string command, ButtonTargets target, int? sourceIndex = null, int ticks = 0)
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
            await ExecuteSingle(part, target, sourceIndex, ticks);
        }
    }

    public AdjustmentValue? GetAdjustmentValue(string command, int? sourceIndex = null)
    {
        string part = FirstChainPart(command);
        if (part == null)
            return null;

        RegisteredCommand registered = _commandRegistry.Get(CommandStringParser.GetName(part));
        if (registered is not { IsAdjustmentCommand: true } || registered.GetValue == null)
            return null;

        using var _routerScope = _router.Enter(_deviceProvider);
        return registered.GetValue(CommandStringParser.GetParameters(part) ?? [], sourceIndex);
    }

    public bool IsAdjustmentCommand(string command)
    {
        string part = FirstChainPart(command);
        if (part == null)
            return false;

        return _commandRegistry.Get(CommandStringParser.GetName(part)) is { IsAdjustmentCommand: true };
    }

    /// <summary>
    /// The first part of a (possibly chained) command string, or null when there is none.
    /// Only that part owns the dial: a wrap's pre/post commands are side actions, not the
    /// adjusted value.
    /// </summary>
    private static string FirstChainPart(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;

        string part = CommandStringParser.SplitChain(command).FirstOrDefault();
        return string.IsNullOrWhiteSpace(part) ? null : part;
    }

    private async Task ExecuteSingle(string command, ButtonTargets target, int? sourceIndex, int ticks)
    {
        if (string.IsNullOrWhiteSpace(command)) return;

        string cleanCommand = CommandStringParser.GetName(command);

        // On a companion the master owns the profile and workspace. Checked here, where every command passes,
        // so no button, macro, CLI call or plugin can switch them on the companion.
        if (CompanionCommandPolicy.IsBlocked(_companions, _device.ScopeKey, cleanCommand))
        {
            Console.WriteLine($"[Companions] '{cleanCommand}' skipped: '{_device.ScopeKey}' is a companion.");
            return;
        }

        RegisteredCommand registered = _commandRegistry.Get(cleanCommand);
        if (registered != null)
        {
            string[] parameters = CommandStringParser.GetParameters(command) ?? [];

            // An adjustment command bound to a dial is driven by the encoder, not by a
            // plain Execute: a turn applies the delta, a press resets. Any other target
            // (touch button, simple button, macro, CLI) falls through to Execute, so the
            // plugin's own Execute stays the single entry point everywhere else.
            if (registered is { IsAdjustmentCommand: true } && target == ButtonTargets.RotaryEncoder)
            {
                if (ticks != 0 && registered.ApplyAdjustment != null)
                {
                    await registered.ApplyAdjustment(parameters, sourceIndex, ticks);
                    return;
                }

                if (ticks == 0 && registered.ApplyReset != null)
                {
                    await registered.ApplyReset(parameters, sourceIndex);
                    return;
                }
            }

            await registered.Execute(parameters, target, sourceIndex);
        }
        else if (_commandRegistry.GetMissingPluginOwner(cleanCommand) is { } owner)
        {
            // A command of a plugin that is not available here is not a shell command: running it
            // through the shell would only fail (or worse, match a program). The binding stays in
            // the config and works again once the plugin is installed and enabled.
            Console.WriteLine(
                $"[Commands] '{cleanCommand}' belongs to plugin '{owner.PluginId}', which is not available - skipped.");
        }
        else
        {
            _commandRunner.EnqueueCommand(command);
        }
    }
}