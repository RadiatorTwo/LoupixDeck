using LoupixDeck.PluginSdk;

namespace LoupixDeck.Services.Plugins;

/// <summary>
/// Console-backed <see cref="IPluginLogger"/> that prefixes every line with the
/// owning plugin's id so plugin output is distinguishable in the app log.
/// </summary>
public sealed class PluginLogger : IPluginLogger
{
    private readonly string _prefix;

    public PluginLogger(string pluginId)
    {
        _prefix = $"[plugin:{pluginId}]";
    }

    public void Info(string message) => Console.WriteLine($"{_prefix} {message}");

    public void Warn(string message) => Console.WriteLine($"{_prefix} WARN: {message}");

    public void Error(string message, Exception exception = null)
    {
        Console.WriteLine($"{_prefix} ERROR: {message}");
        if (exception != null)
            Console.WriteLine($"{_prefix} {exception}");
    }
}
