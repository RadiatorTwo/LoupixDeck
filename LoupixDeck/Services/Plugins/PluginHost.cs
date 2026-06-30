using System.Diagnostics;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Services.Plugins;

/// <summary>
/// Concrete <see cref="IPluginHost"/> handed to a single plugin. The host
/// operations are wired as delegates by the <see cref="PluginManager"/> so the
/// host stays decoupled from the core's command and rendering services.
/// </summary>
public sealed class PluginHost : IPluginHost
{
    private readonly Action<string> _executeCommand;
    private readonly Action<string> _requestButtonRefresh;
    private readonly Action<IFolderProvider> _openFolder;
    private readonly Action<int, string, TimeSpan> _overlayTouchText;
    private readonly Func<int, int> _getTouchSlotForRotary;
    private readonly Func<IExclusiveModeProvider, bool> _requestExclusiveMode;
    private readonly Action<IExclusiveModeProvider> _releaseExclusiveMode;
    private readonly Func<bool> _isInExclusiveMode;
    private readonly Func<string, IReadOnlyList<string>> _getButtonStates;
    private readonly Func<string, string> _getActiveButtonState;
    private readonly Func<string, string, bool> _setActiveButtonState;

    public PluginHost(
        IPluginLogger logger,
        IPluginSettings settings,
        DeviceInfo activeDevice,
        Action<string> executeCommand,
        Action<string> requestButtonRefresh,
        Action<IFolderProvider> openFolder,
        Action<int, string, TimeSpan> overlayTouchText,
        Func<int, int> getTouchSlotForRotary,
        Func<IExclusiveModeProvider, bool> requestExclusiveMode,
        Action<IExclusiveModeProvider> releaseExclusiveMode,
        Func<bool> isInExclusiveMode,
        Func<string, IReadOnlyList<string>> getButtonStates,
        Func<string, string> getActiveButtonState,
        Func<string, string, bool> setActiveButtonState)
    {
        Logger = logger;
        Settings = settings;
        ActiveDevice = activeDevice;
        _executeCommand = executeCommand;
        _requestButtonRefresh = requestButtonRefresh;
        _openFolder = openFolder;
        _overlayTouchText = overlayTouchText;
        _getTouchSlotForRotary = getTouchSlotForRotary;
        _requestExclusiveMode = requestExclusiveMode;
        _releaseExclusiveMode = releaseExclusiveMode;
        _isInExclusiveMode = isInExclusiveMode;
        _getButtonStates = getButtonStates;
        _getActiveButtonState = getActiveButtonState;
        _setActiveButtonState = setActiveButtonState;
    }

    public IPluginLogger Logger { get; }

    public IPluginSettings Settings { get; }

    public DeviceInfo ActiveDevice { get; }

    public void RequestButtonRefresh(string commandName) => _requestButtonRefresh?.Invoke(commandName);

    public void ExecuteCommand(string command) => _executeCommand?.Invoke(command);

    public void OpenFolder(IFolderProvider provider) => _openFolder?.Invoke(provider);

    public void OverlayTouchText(int slot, string text, TimeSpan duration) =>
        _overlayTouchText?.Invoke(slot, text, duration);

    public int GetTouchSlotForRotary(int rotaryIndex) =>
        _getTouchSlotForRotary?.Invoke(rotaryIndex) ?? -1;

    public bool RequestExclusiveMode(IExclusiveModeProvider provider) =>
        _requestExclusiveMode?.Invoke(provider) ?? false;

    public void ReleaseExclusiveMode(IExclusiveModeProvider provider) =>
        _releaseExclusiveMode?.Invoke(provider);

    public bool IsInExclusiveMode => _isInExclusiveMode?.Invoke() ?? false;

    public IReadOnlyList<string> GetButtonStates(string commandName) =>
        _getButtonStates?.Invoke(commandName) ?? [];

    public string GetActiveButtonState(string commandName) =>
        _getActiveButtonState?.Invoke(commandName);

    public bool SetActiveButtonState(string commandName, string stateNameOrId) =>
        _setActiveButtonState?.Invoke(commandName, stateNameOrId) ?? false;

    public bool OpenBrowser(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        try
        {
            // UseShellExecute=true routes through the OS handler: default
            // browser on Windows, xdg-open (via shell) on Linux. Works
            // headlessly when there's no UI, in which case Start returns
            // null but the dispatch is still considered attempted.
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            Logger?.Error($"OpenBrowser failed for '{url}'", ex);
            return false;
        }
    }
}
