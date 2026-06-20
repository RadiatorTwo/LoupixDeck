namespace LoupixDeck.PluginSdk;

/// <summary>A single positional command parameter: its name and CLR type.</summary>
public sealed class CommandParameter(string name, Type parameterType)
{
    public string Name { get; } = name;
    public Type ParameterType { get; } = parameterType;
}

/// <summary>
/// Declarative description of a plugin command. Replaces the core's
/// <c>[Command]</c> attribute. The <see cref="CommandName"/> is the stable
/// identifier persisted in button assignments — it MUST never change once a
/// plugin has shipped, otherwise existing <c>config.json</c> files break.
/// </summary>
public sealed class CommandDescriptor
{
    /// <summary>Stable command identifier, e.g. <c>System.ObsStartRecord</c>.</summary>
    public required string CommandName { get; init; }

    /// <summary>Label shown in the command-selection menu.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Group/category the command is listed under in the menu.</summary>
    public required string Group { get; init; }

    /// <summary>Parameter placeholder template, e.g. <c>({SceneName})</c>.
    /// Null when the command takes no parameters.</summary>
    public string? ParameterTemplate { get; init; }

    /// <summary>Positional parameter definitions, in declaration order.</summary>
    public IReadOnlyList<CommandParameter> Parameters { get; init; } = [];

    /// <summary>
    /// When true the command is not listed as a plain leaf in the command
    /// selection menu — it is instead surfaced through a dynamic submenu the
    /// plugin builds via <see cref="IMenuContributor"/> (e.g. one entry per OBS
    /// scene). The command stays fully registered and executable.
    /// </summary>
    public bool HiddenFromMenu { get; init; }
}
