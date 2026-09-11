using LoupixDeck.Models;

namespace LoupixDeck.Services.DialPresets;

/// <summary>
/// The dial presets the user has saved. Shared app-wide and persisted in <c>dial-presets.json</c>
/// next to the macros, rather than in a device config, so a preset saved on one deck can be applied
/// on another.
/// </summary>
/// <remarks>
/// The built-in presets are not in here: which of them an installation can run depends on the
/// command registry, which is per device. <see cref="IDialPresetCatalog"/> is the combined list.
/// </remarks>
public interface IDialPresetStore
{
    /// <summary>The user's presets, in the order they were created.</summary>
    IReadOnlyList<DialPreset> Presets { get; }

    /// <summary>Raised after any change, so the panel and the menus can re-read the list.</summary>
    event EventHandler PresetsChanged;

    /// <summary>Reads the file. A missing file is the normal first-run state and yields no user
    /// presets; an unreadable one is backed up and treated the same way.</summary>
    void Load();

    /// <summary>Stores <paramref name="preset"/> as a new user preset and saves. Ignored for a
    /// preset without a name or without a single assigned gesture.</summary>
    void Add(DialPreset preset);

    /// <summary>Overwrites the stored preset with the same <see cref="DialPreset.Id"/> and saves.
    /// Ignored when no such user preset exists.</summary>
    void Update(DialPreset preset);

    /// <summary>Removes the user preset with this id and saves. Dials configured from it are not
    /// touched — a preset is a one-shot loader, not a live reference.</summary>
    void Remove(Guid id);

    /// <summary>True when a name is usable: non-empty, and not already taken by another user
    /// preset or by a built-in one.</summary>
    bool IsNameValid(string name, DialPreset ignore = null);
}

/// <summary>
/// Every dial preset this device can offer: the built-in ones it can actually run, followed by the
/// user's own. One place to ask, so the quick menu, the command catalogue and the actions panel
/// cannot end up showing different lists.
/// </summary>
public interface IDialPresetCatalog
{
    IReadOnlyList<DialPreset> Presets { get; }

    /// <summary>Raised when the user's presets change. The built-ins never do.</summary>
    event EventHandler PresetsChanged;
}
