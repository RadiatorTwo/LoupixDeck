using LoupixDeck.PluginSdk;

namespace LoupixDeck.Services.Plugins;

/// <summary>
/// Concrete <see cref="IPluginHost"/> handed to a single plugin. The two host
/// operations are wired as delegates by the <see cref="PluginManager"/> so the
/// host stays decoupled from the core's command and rendering services.
/// </summary>
public sealed class PluginHost : IPluginHost
{
    private readonly Action<string> _executeCommand;
    private readonly Action<string> _requestButtonRefresh;
    private readonly Action<IFolderProvider> _openFolder;

    public PluginHost(
        IPluginLogger logger,
        IPluginSettings settings,
        DeviceInfo activeDevice,
        Action<string> executeCommand,
        Action<string> requestButtonRefresh,
        Action<IFolderProvider> openFolder)
    {
        Logger = logger;
        Settings = settings;
        ActiveDevice = activeDevice;
        _executeCommand = executeCommand;
        _requestButtonRefresh = requestButtonRefresh;
        _openFolder = openFolder;
    }

    public IPluginLogger Logger { get; }

    public IPluginSettings Settings { get; }

    public DeviceInfo ActiveDevice { get; }

    public void RequestButtonRefresh(string commandName) => _requestButtonRefresh?.Invoke(commandName);

    public void ExecuteCommand(string command) => _executeCommand?.Invoke(command);

    public void OpenFolder(IFolderProvider provider) => _openFolder?.Invoke(provider);
}
