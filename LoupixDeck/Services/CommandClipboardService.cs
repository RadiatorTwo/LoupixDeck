using LoupixDeck.Models;

namespace LoupixDeck.Services;

/// <summary>
/// In-app clipboard for the commands of a sequence (issue #684): the calls of the copied chips,
/// each with the values the user set, exactly. A call is immutable and so its own snapshot: the copy
/// survives closing the editor and later edits of the source. Registered as a root singleton and
/// forwarded into every device provider, so commands paste across buttons, editors and devices.
/// </summary>
public interface ICommandClipboardService
{
    /// <summary>The copied commands in sequence order; empty when nothing is copied.</summary>
    IReadOnlyList<CommandCall> Commands { get; }

    bool HasData { get; }

    /// <summary>Replaces the clipboard with <paramref name="commands"/>. Blank free-text entries are
    /// dropped, and a copy of nothing leaves the clipboard as it was.</summary>
    void Copy(IEnumerable<CommandCall> commands);
}

public sealed class CommandClipboardService : ICommandClipboardService
{
    public IReadOnlyList<CommandCall> Commands { get; private set; } = [];

    public bool HasData => Commands.Count > 0;

    public void Copy(IEnumerable<CommandCall> commands)
    {
        List<CommandCall> copied = commands?
            .Where(c => c != null && !(c.IsText && string.IsNullOrWhiteSpace(c.Text))).ToList() ?? [];
        if (copied.Count == 0) return;

        Commands = copied;
    }
}
