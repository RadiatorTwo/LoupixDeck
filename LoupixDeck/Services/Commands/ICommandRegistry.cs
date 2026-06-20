using LoupixDeck.PluginSdk;

namespace LoupixDeck.Services.Commands;

/// <summary>
/// Single source of truth for every command available to the app — core and
/// plugin alike. Replaces direct use of <c>ISysCommandService</c> in the
/// command pipeline, command builder, dynamic-text manager and menu building.
/// </summary>
public interface ICommandRegistry
{
    /// <summary>(Re)builds the command table from all registered providers.</summary>
    void Initialize();

    /// <summary>True when a command with the given name is registered.</summary>
    bool Contains(string commandName);

    /// <summary>Returns the command, or null when it is not registered.</summary>
    RegisteredCommand Get(string commandName);

    /// <summary>Returns all registered commands.</summary>
    IEnumerable<RegisteredCommand> GetAll();

    /// <summary>
    /// Executes a command by name. <paramref name="target"/> is the button type
    /// that triggered the call (or <see cref="ButtonTargets.None"/> when the
    /// origin is not a button — CLI, plugin-to-plugin chaining, etc.).
    /// <paramref name="sourceIndex"/> identifies the originating control
    /// (rotary index, touch slot, simple button index) when the target is an
    /// indexed source, so plugins can locate the physical control that fired.
    /// </summary>
    Task Execute(string commandName, string[] parameters, ButtonTargets target, int? sourceIndex = null);
}
