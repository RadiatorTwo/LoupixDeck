using LoupixDeck.Models.Layers;
using LoupixDeck.Utils;

namespace LoupixDeck.Services.Plugins;

/// <summary>
/// The plugins that reported themselves unavailable through <c>IPluginHost.SetAvailable</c>,
/// for example because they lost the connection to the application they control. The renderer
/// asks it for every touch button, so a button bound to one of their commands is drawn dimmed
/// and marked. Global, like the plugins themselves: one plugin instance serves every device.
/// </summary>
public static class PluginAvailability
{
    private static readonly object Gate = new();

    // Plugin id → its command names. Registered once the plugin's commands are known, which is
    // after Initialize — where a plugin typically reports itself unavailable first.
    private static readonly Dictionary<string, IReadOnlyList<string>> CommandsByPlugin =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> UnavailablePlugins = new(StringComparer.OrdinalIgnoreCase);

    // Every command of an unavailable plugin, swapped as a whole so the render threads read it
    // without taking the gate.
    private static volatile HashSet<string> _unavailableCommands = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Records whether <paramref name="pluginId"/> is available. Returns true when the buttons
    /// have to be redrawn.
    /// </summary>
    public static bool Set(string pluginId, bool available)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
            return false;

        lock (Gate)
        {
            bool changed = available ? UnavailablePlugins.Remove(pluginId) : UnavailablePlugins.Add(pluginId);
            return changed && Rebuild();
        }
    }

    /// <summary>
    /// Records the command names of a loaded plugin. Returns true when the buttons have to be
    /// redrawn (the plugin already reported itself unavailable).
    /// </summary>
    public static bool RegisterCommands(string pluginId, IEnumerable<string> commandNames)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
            return false;

        lock (Gate)
        {
            CommandsByPlugin[pluginId] = (commandNames ?? []).Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
            return UnavailablePlugins.Contains(pluginId) && Rebuild();
        }
    }

    /// <summary>
    /// Forgets an unloaded plugin: its buttons lose the mark. Returns true when the buttons have
    /// to be redrawn.
    /// </summary>
    public static bool Forget(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
            return false;

        lock (Gate)
        {
            CommandsByPlugin.Remove(pluginId);
            return UnavailablePlugins.Remove(pluginId) && Rebuild();
        }
    }

    /// <summary>True when any command of <paramref name="boundCommand"/> belongs to an unavailable plugin.</summary>
    public static bool IsUnavailable(string boundCommand)
    {
        HashSet<string> commands = _unavailableCommands;
        if (commands.Count == 0 || string.IsNullOrWhiteSpace(boundCommand))
            return false;

        foreach (string segment in CommandStringParser.SplitChain(boundCommand))
        {
            if (commands.Contains(PluginLayerKey.ParseCommandName(segment)))
                return true;
        }

        return false;
    }

    /// <summary>Rebuilds the snapshot under the gate; true when it changed.</summary>
    private static bool Rebuild()
    {
        HashSet<string> commands = new(
            UnavailablePlugins.SelectMany(id => CommandsByPlugin.TryGetValue(id, out IReadOnlyList<string> names) ? names : []),
            StringComparer.OrdinalIgnoreCase);

        if (commands.SetEquals(_unavailableCommands))
            return false;

        _unavailableCommands = commands;
        return true;
    }
}
