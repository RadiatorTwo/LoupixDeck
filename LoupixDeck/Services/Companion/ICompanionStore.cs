using LoupixDeck.Models.Companion;

namespace LoupixDeck.Services.Companion;

/// <summary>
/// Reads and writes <c>companions.json</c> in the config directory. IO only — the rules live in
/// <see cref="CompanionGroupValidator"/>, the runtime logic in <see cref="ICompanionCoordinator"/>.
/// </summary>
public interface ICompanionStore
{
    /// <summary>Loads the file, or returns an empty config when it does not exist. A corrupted file is
    /// backed up and treated as empty, never as a reason to fail start-up.</summary>
    CompanionConfig Load();

    void Save(CompanionConfig config);
}
