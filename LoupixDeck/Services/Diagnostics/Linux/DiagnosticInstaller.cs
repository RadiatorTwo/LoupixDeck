namespace LoupixDeck.Services.Diagnostics.Linux;

/// <summary>
/// The installation script as the one repair routine for everything system-wide (issue #258
/// phase 4). Device Doctor never runs it: it shows the exact command, because these are root
/// operations and the script stays the single source of truth for system-wide installation.
///
/// The command is built from the script's real location when it can be found, so it can be
/// pasted as it stands instead of only working from the directory the user happens to be in.
/// </summary>
public static class DiagnosticInstaller
{
    private const string ScriptName = "install-loupixdeck.sh";

    /// <summary>The exact command to run, with the script's path when it was found.</summary>
    public static string Command()
    {
        string path = Locate();

        return path == null ? $"sudo ./{ScriptName}" : $"sudo {Quote(path)}";
    }

    /// <summary>The script's absolute path, or null when it is not next to the app or the source.</summary>
    public static string Locate()
    {
        foreach (string directory in Candidates())
        {
            try
            {
                string candidate = Path.Combine(directory, ScriptName);

                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch (Exception)
            {
                // An unreadable candidate directory says nothing. Keep looking.
            }
        }

        return null;
    }

    /// <summary>
    /// Where the script can be: next to the executable (a published build ships it), in the
    /// working directory, and one and two levels up from the binary, which is where it sits in
    /// a source build running out of bin/Debug/net9.0.
    /// </summary>
    private static IEnumerable<string> Candidates()
    {
        string baseDirectory = AppContext.BaseDirectory;

        yield return baseDirectory;
        yield return Directory.GetCurrentDirectory();

        string current = baseDirectory;

        for (int level = 0; level < 5; level++)
        {
            current = Path.GetDirectoryName(current?.TrimEnd('/'));

            if (string.IsNullOrEmpty(current))
            {
                yield break;
            }

            yield return current;
        }
    }

    private static string Quote(string path) => path.Contains(' ') ? $"\"{path}\"" : path;
}
