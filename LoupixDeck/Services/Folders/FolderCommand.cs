using LoupixDeck.Models;

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

    public static string Build(Guid folderId) => $"{OpenName}({folderId})";

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

    /// <summary>True when <paramref name="call"/> opens a folder; returns its id.</summary>
    public static bool TryParse(CommandCall call, out Guid folderId)
    {
        folderId = Guid.Empty;
        if (call == null) return false;
        if (call.IsText) return TryParseSegment(call.Text, out folderId);

        string[] parameters = call.GetParameters();
        return string.Equals(call.Name, OpenName, StringComparison.Ordinal)
            && parameters.Length == 1
            && Guid.TryParse(parameters[0].Trim(), out folderId);
    }

    /// <summary>The ids of every folder the command sequence opens.</summary>
    public static IEnumerable<Guid> ReferencedFolders(CommandBinding command)
    {
        foreach (CommandCall call in command ?? CommandBinding.Empty)
            if (TryParse(call, out Guid id))
                yield return id;
    }

    /// <summary>True when any segment of the command sequence opens a folder.</summary>
    public static bool OpensFolder(CommandBinding command) => ReferencedFolders(command).Any();

    /// <summary>
    /// Removes the calls that open one of <paramref name="folderIds"/>. Returns the remaining
    /// sequence, which is empty when nothing is left.
    /// </summary>
    public static CommandBinding RemoveReferences(CommandBinding command, IReadOnlySet<Guid> folderIds)
    {
        if (CommandBinding.IsNullOrEmpty(command)) return command;

        return new CommandBinding(command.Where(c => !(TryParse(c, out Guid id) && folderIds.Contains(id))));
    }
}
