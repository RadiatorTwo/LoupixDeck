#nullable enable
using System.Collections.Immutable;

namespace LoupixDeck.Commands.Base;

public enum CommandPlatform
{
    All,
    Windows,
    Linux
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class CommandAttribute(
    string commandName,
    string displayName,
    string group) : Attribute
{
    public string CommandName { get; } = commandName;
    public string DisplayName { get; } = displayName;
    public string Group { get; } = group;
    public string? ParameterTemplate { get; }

    public ImmutableArray<(string Name, Type Type)> Parameters { get; }

    public CommandPlatform Platform { get; init; } = CommandPlatform.All;

    /// <summary>
    /// When true the command is registered and remains executable (a button can
    /// still be assigned <c>Name(args)</c> manually, and the pipeline runs it),
    /// but it is not listed in the command-selection menu. Use for internal /
    /// developer commands that should not be user-discoverable.
    /// </summary>
    public bool Hidden { get; set; }

    public CommandAttribute(
        string commandName,
        string displayName,
        string group,
        string parameterTemplate,
        string[] parameterNames,
        Type[] parameterTypes) : this(commandName, displayName, group)
    {
        ArgumentException.ThrowIfNullOrEmpty(parameterTemplate);
        ArgumentNullException.ThrowIfNull(parameterTypes);
        ArgumentNullException.ThrowIfNull(parameterNames);

        if (parameterNames.Length != parameterTypes.Length)
            throw new ArgumentException("The number of parameter names and types must match.");

        ParameterTemplate = parameterTemplate;
        Parameters = parameterNames.Zip(parameterTypes).ToImmutableArray();
    }
}
