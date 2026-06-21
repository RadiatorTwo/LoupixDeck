using System.Diagnostics;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Services.Plugins;

/// <summary>
/// Concrete <see cref="IPluginHost"/> handed to a single plugin. The host
/// operations are wired as delegates by the <see cref="PluginManager"/> so the
/// host stays decoupled from the core's command and rendering services.
/// </summary>
public sealed class PluginHost(
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
    Func<bool> isInExclusiveMode) : IPluginHost
{
    public IPluginLogger Logger { get; } = logger;

    public IPluginSettings Settings { get; } = settings;

    public DeviceInfo ActiveDevice { get; } = activeDevice;

    public void RequestButtonRefresh(string commandName) => requestButtonRefresh?.Invoke(commandName);

    public void ExecuteCommand(string command) => executeCommand?.Invoke(command);

    public void OpenFolder(IFolderProvider provider) => openFolder?.Invoke(provider);

    public void OverlayTouchText(int slot, string text, TimeSpan duration) =>
        overlayTouchText?.Invoke(slot, text, duration);

    public int GetTouchSlotForRotary(int rotaryIndex) =>
        getTouchSlotForRotary?.Invoke(rotaryIndex) ?? -1;

    public bool RequestExclusiveMode(IExclusiveModeProvider provider) =>
        requestExclusiveMode?.Invoke(provider) ?? false;

    public void ReleaseExclusiveMode(IExclusiveModeProvider provider) =>
        releaseExclusiveMode?.Invoke(provider);

    public bool IsInExclusiveMode => isInExclusiveMode?.Invoke() ?? false;

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
