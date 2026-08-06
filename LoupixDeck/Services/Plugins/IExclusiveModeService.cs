using LoupixDeck.PluginSdk;

namespace LoupixDeck.Services.Plugins;

/// <summary>
/// Global single-owner exclusive-mode coordinator. Mirrors the
/// <see cref="FolderNavigation.IFolderNavigationService"/> contract but takes the
/// device over: while a provider is active, the controller must route the hardware
/// inputs it owns to it and suppress the normal page rendering it owns.
/// </summary>
/// <remarks>
/// Ownership is per control category (issue #127): a provider declares an
/// <see cref="IExclusiveModeProvider.Scope"/> and everything outside it keeps its
/// normal page assignments. Guard sites therefore ask <see cref="Owns"/> for the
/// category they are about to touch rather than the blanket <see cref="IsActive"/>.
/// </remarks>
public interface IExclusiveModeService
{
    /// <summary>True while a provider currently owns the device — regardless of how
    /// much of it. Use <see cref="Owns"/> to decide whether a specific control is taken.</summary>
    bool IsActive { get; }

    /// <summary>The currently active provider, or null when inactive.</summary>
    IExclusiveModeProvider Current { get; }

    /// <summary>The active provider's declared scope, or <see cref="ExclusiveControlScope.None"/>
    /// when inactive. Read fresh from the provider on every access, so a provider may
    /// change what it overrides while running.</summary>
    ExclusiveControlScope ActiveScope { get; }

    /// <summary>True when a provider is active and owns every flag in
    /// <paramref name="scope"/>. Always false when inactive or for
    /// <see cref="ExclusiveControlScope.None"/>.</summary>
    bool Owns(ExclusiveControlScope scope);

    /// <summary>Tries to enter exclusive mode. Returns false if already active.</summary>
    bool TryEnter(IExclusiveModeProvider provider);

    /// <summary>Releases exclusive mode. No-op if <paramref name="provider"/>
    /// is not the current owner.</summary>
    void Exit(IExclusiveModeProvider provider);

    /// <summary>Fired when the active provider changes or raises EntriesChanged.</summary>
    event Action StateChanged;
}
