using LoupixDeck.Utils;

namespace LoupixDeck.Services.Commands;

public static class CommandRegistryExtensions
{
    /// <summary>
    /// True when <paramref name="command"/>, a bound command string, draws a picture of its own onto the
    /// key: an image or animated display command, or a command of a plugin that is not loaded and so
    /// may be one. Text display commands do not count; they fill a text layer rather than drawing over it.
    /// </summary>
    public static bool DrawsOnKey(this ICommandRegistry registry, string command)
    {
        string name = string.IsNullOrWhiteSpace(command) ? null : CommandStringParser.GetName(command);
        if (registry == null || string.IsNullOrEmpty(name))
            return false;

        RegisteredCommand registered = registry.Get(name);
        if (registered == null)
            return registry.GetMissingPluginOwner(name) != null;

        return (registered.IsImageDisplayCommand && registered.RenderImage != null)
            || (registered.IsAnimatedImageCommand && registered.RenderAnimatedFrame != null);
    }
}
