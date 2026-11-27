using LoupixDeck.Models.Converter;
using Newtonsoft.Json.Linq;

namespace LoupixDeck.Services;

/// <summary>
/// Keeps one copy of a file as it was before the first structured command (array form) was written
/// into it. An app older than structured storage cannot read that form and moves the file aside as
/// corrupted; with the copy the user can go back to it.
/// </summary>
/// <remarks>
/// The copy is <c>&lt;file&gt;.prestructured.&lt;timestamp&gt;.bak</c>, next to the file. It is made
/// once: not when such a copy already exists, and not when the file on disk already holds the array
/// form. A failed copy is logged and does not stop the save, since dropping the user's edit would be
/// worse than missing the copy.
/// </remarks>
public static class StructuredCommandBackup
{
    private const string Marker = ".prestructured.";

    /// <summary>
    /// Call before <paramref name="filePath"/> is replaced. <paramref name="writesArrayForm"/> says
    /// whether the new content holds a chain in array form.
    /// </summary>
    public static void BeforeSave(string filePath, bool writesArrayForm)
    {
        if (!writesArrayForm || string.IsNullOrWhiteSpace(filePath))
            return;

        try
        {
            if (!File.Exists(filePath))
                return;

            string directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
            string name = Path.GetFileName(filePath);
            if (Directory.EnumerateFiles(directory, name + Marker + "*.bak").Any())
                return;

            if (CommandBindingJsonConverter.ContainsArrayForm(JToken.Parse(File.ReadAllText(filePath))))
                return;

            string backup = $"{filePath}{Marker}{DateTime.Now:yyyyMMdd_HHmmss}.bak";
            if (File.Exists(backup))
                return;

            File.Copy(filePath, backup);
            Console.WriteLine($"[Config] '{name}' backed up to '{backup}' before its first structured command.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config] backup of '{filePath}' before its first structured command failed: {ex.Message}");
        }
    }
}
