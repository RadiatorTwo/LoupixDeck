namespace LoupixDeck.Setup.Services;

/// <summary>Small filesystem helpers with retry, used by install/update/uninstall.</summary>
public static class FileOps
{
    /// <summary>Recursively deletes a directory, retrying briefly to ride out transient locks.</summary>
    public static bool TryDeleteDirectory(string dir, int attempts = 10, int delayMs = 300)
    {
        if (!Directory.Exists(dir))
            return true;

        for (int i = 0; i < attempts; i++)
        {
            try
            {
                Directory.Delete(dir, recursive: true);
                return true;
            }
            catch
            {
                if (i == attempts - 1)
                    return false;
                Thread.Sleep(delayMs);
            }
        }

        return false;
    }

    /// <summary>Total size of a directory tree in bytes (best effort).</summary>
    public static long DirectorySize(string dir)
    {
        long total = 0;
        try
        {
            foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; }
                catch { /* skip unreadable */ }
            }
        }
        catch
        {
            // directory vanished / inaccessible
        }
        return total;
    }
}
