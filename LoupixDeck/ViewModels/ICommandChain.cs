using System.Collections.ObjectModel;

namespace LoupixDeck.ViewModels;

/// <summary>
/// A command sequence shown as a strip of editable chips: one <see cref="CommandSequenceSlot"/>, or
/// the single sequence of a simple button. The command context menu (issue #684) works through it,
/// so every editor gets the same copy and paste entries.
/// </summary>
public interface ICommandChain
{
    /// <summary>The chips in execution order.</summary>
    ObservableCollection<CommandSegment> Commands { get; }

    /// <summary>Inserts a call at the given chip index, exactly as given. Returns false when there
    /// was nothing to insert.</summary>
    bool InsertCallAt(Models.CommandCall call, int index);

    void RemoveSegment(CommandSegment segment);
}
