namespace LoupixDeck.Setup.Services;

/// <summary>How the previous version was preserved before an update wrote the new one.</summary>
public enum BackupMode
{
    /// <summary>The install dir was renamed aside; the update writes into a fresh directory.</summary>
    Moved,

    /// <summary>The install dir was copied aside and stays in place; the update overwrites it.</summary>
    Copied
}

/// <summary>Small filesystem helpers with retry, used by install/update/uninstall.</summary>
public static class FileOps
{
    /// <summary>
    /// Renames a directory, retrying briefly to ride out transient locks. Returns false instead of
    /// throwing when the directory cannot be renamed at all — on Windows that is the case whenever any
    /// process holds it, most notably as its current directory, which no retry will resolve.
    /// </summary>
    public static bool TryMoveDirectory(string source, string destination, int attempts = 5, int delayMs = 300)
    {
        for (int i = 0; i < attempts; i++)
        {
            try
            {
                Directory.Move(source, destination);
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

    /// <summary>Recursively copies a directory tree, overwriting existing files.</summary>
    public static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (string dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));

        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            File.Copy(file, target, overwrite: true);
            File.SetAttributes(target, FileAttributes.Normal);
        }
    }

    /// <summary>
    /// Deletes everything inside a directory but keeps the directory itself — the restore path for an
    /// in-place update, where the install dir is held by another process and cannot be removed.
    /// Best effort per entry: a leftover that can't be deleted must not abort the restore.
    /// </summary>
    public static void DeleteDirectoryContents(string dir)
    {
        if (!Directory.Exists(dir))
            return;

        foreach (string file in Directory.GetFiles(dir))
        {
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }
            catch
            {
                // best effort
            }
        }

        foreach (string sub in Directory.GetDirectories(dir))
            TryDeleteDirectory(sub, attempts: 3, delayMs: 100);
    }

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
