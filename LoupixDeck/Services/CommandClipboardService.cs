namespace LoupixDeck.Services;

/// <summary>
/// In-app clipboard for the commands of a sequence (issue #684): the raw command strings of the
/// copied chips, each with the values the user set. A string is its own snapshot, so the copy
/// survives closing the editor and later edits of the source. Registered as a root singleton and
/// forwarded into every device provider, so commands paste across buttons, editors and devices.
/// </summary>
public interface ICommandClipboardService
{
    /// <summary>The copied commands in sequence order; empty when nothing is copied.</summary>
    IReadOnlyList<string> Commands { get; }

    bool HasData { get; }

    /// <summary>Replaces the clipboard with <paramref name="commands"/>. Blank entries are dropped,
    /// and a copy of nothing leaves the clipboard as it was.</summary>
    void Copy(IEnumerable<string> commands);
}

public sealed class CommandClipboardService : ICommandClipboardService
{
    public IReadOnlyList<string> Commands { get; private set; } = [];

    public bool HasData => Commands.Count > 0;

    public void Copy(IEnumerable<string> commands)
    {
        List<string> copied = commands?.Where(c => !string.IsNullOrWhiteSpace(c)).ToList() ?? [];
        if (copied.Count == 0) return;

        Commands = copied;
    }
}
