namespace LoupixDeck.Models.Layers;

/// <summary>
/// Builds the canonical owner key used to bind a plugin-managed layer to the command
/// that drives it (see <see cref="LayerBase.OwnerKey"/>). The key is derived from a
/// button's bound command string (e.g. <c>"Argus.Sensor(CPU:0)"</c>) and normalized so
/// it matches identically at materialization time (in the dynamic-text manager) and at
/// orphan-sweep time, regardless of incidental whitespace. Two buttons binding the same
/// command with different parameters yield different keys, so each targets its own layer.
/// </summary>
public static class PluginLayerKey
{
    /// <summary>
    /// Returns the normalized <c>name(p1,p2,…)</c> key for a bound command string, or the
    /// bare <c>name</c> when it has no parameters. Returns <c>null</c> for blank input.
    /// </summary>
    public static string For(string boundCommand)
    {
        if (string.IsNullOrWhiteSpace(boundCommand))
            return null;

        var name = ParseCommandName(boundCommand);
        if (string.IsNullOrEmpty(name))
            return null;

        var parameters = ParseParameters(boundCommand);
        return parameters.Length == 0
            ? name
            : $"{name}({string.Join(",", parameters)})";
    }

    /// <summary>The command name (everything before the first <c>'('</c>), trimmed.</summary>
    public static string ParseCommandName(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return string.Empty;

        var open = command.IndexOf('(');
        return (open == -1 ? command : command[..open]).Trim();
    }

    /// <summary>The trimmed, empty-removed parameters between the parentheses.</summary>
    public static string[] ParseParameters(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return [];

        var start = command.IndexOf('(');
        var end = command.IndexOf(')');
        if (start == -1 || end == -1 || end <= start)
            return [];

        var inner = command.Substring(start + 1, end - start - 1);
        return inner.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
