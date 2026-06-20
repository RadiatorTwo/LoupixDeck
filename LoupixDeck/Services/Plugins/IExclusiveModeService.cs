using LoupixDeck.PluginSdk;

namespace LoupixDeck.Services.Plugins;

/// <summary>
/// Global single-owner exclusive-mode coordinator. Mirrors the
/// <see cref="FolderNavigation.IFolderNavigationService"/> contract but takes the
/// device over completely: while a provider is active, the controller must route
/// all hardware inputs to it and suppress normal page rendering.
/// </summary>
public interface IExclusiveModeService
{
    /// <summary>True while a provider currently owns the device.</summary>
    bool IsActive { get; }

    /// <summary>The currently active provider, or null when inactive.</summary>
    IExclusiveModeProvider Current { get; }

    /// <summary>Tries to enter exclusive mode. Returns false if already active.</summary>
    bool TryEnter(IExclusiveModeProvider provider);

    /// <summary>Releases exclusive mode. No-op if <paramref name="provider"/>
    /// is not the current owner.</summary>
    void Exit(IExclusiveModeProvider provider);

    /// <summary>Fired when the active provider changes or raises EntriesChanged.</summary>
    event Action StateChanged;
}
