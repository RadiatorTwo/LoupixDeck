namespace LoupixDeck.Services.Folders;

/// <summary>
/// Builds and reads the command string that links a button to a custom folder (issue #249).
/// A folder button stores <c>System.OpenFolder(&lt;folder id&gt;)</c> like any other command, so
/// wraps, copy/paste and export treat it as ordinary button content.
/// </summary>
public static class FolderCommand
{
    public const string OpenName = "System.OpenFolder";
    public const string BackName = "System.FolderBack";
    public const string CloseName = "System.CloseFolders";

    private const string SequenceSeparator = "&&";

    public static string Build(Guid folderId) => $"{OpenName}({folderId})";

    /// <summary>The segments of a command sequence, trimmed, without empty entries.</summary>
    public static IEnumerable<string> Segments(string command)
        => string.IsNullOrWhiteSpace(command)
            ? []
            : command.Split(SequenceSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>True when the single command <paramref name="segment"/> opens a folder; returns its id.</summary>
    public static bool TryParseSegment(string segment, out Guid folderId)
    {
        folderId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(segment)) return false;

        string trimmed = segment.Trim();
        if (!trimmed.StartsWith(OpenName + "(", StringComparison.Ordinal) || !trimmed.EndsWith(')'))
            return false;

        string argument = trimmed[(OpenName.Length + 1)..^1].Trim();
        return Guid.TryParse(argument, out folderId);
    }

    /// <summary>The ids of every folder the command sequence opens.</summary>
    public static IEnumerable<Guid> ReferencedFolders(string command)
    {
        foreach (string segment in Segments(command))
            if (TryParseSegment(segment, out Guid id))
                yield return id;
    }

    /// <summary>True when any segment of the command sequence opens a folder.</summary>
    public static bool OpensFolder(string command) => ReferencedFolders(command).Any();

    /// <summary>
    /// Removes the segments that open one of <paramref name="folderIds"/>. Returns the remaining
    /// sequence, or an empty string when nothing is left.
    /// </summary>
    public static string RemoveReferences(string command, IReadOnlySet<Guid> folderIds)
    {
        if (string.IsNullOrWhiteSpace(command)) return command;

        List<string> kept = [.. Segments(command).Where(s => !(TryParseSegment(s, out Guid id) && folderIds.Contains(id)))];
        return string.Join($" {SequenceSeparator} ", kept);
    }
}
