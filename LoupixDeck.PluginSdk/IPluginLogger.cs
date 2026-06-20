namespace LoupixDeck.PluginSdk;

/// <summary>Logging sink the host provides to a plugin via <see cref="IPluginHost"/>.</summary>
public interface IPluginLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
}
