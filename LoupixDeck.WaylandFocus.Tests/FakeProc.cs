namespace LoupixDeck.WaylandFocus.Tests;

/// <summary>
/// Fake /proc tree (and optional desktop-file dirs) for resolution tests:
/// unit tests never depend on the machine's live process table.
/// </summary>
internal sealed class FakeProc : IDisposable
{
    private int _nextDesktopDir;

    public string Root { get; } =
        Directory.CreateTempSubdirectory("focuswatcher-proc-").FullName;

    public FakeProc AddProcess(int pid, string comm, params string[] arguments)
        => AddProcessRaw(pid, comm, string.Join('\0', arguments) + "\0");

    /// <summary>Adds a process whose cmdline is served verbatim (e.g. space-rewritten argv).</summary>
    public FakeProc AddProcessRaw(int pid, string comm, string rawCmdline)
    {
        var dir = CreateEntry(pid.ToString());
        File.WriteAllText(Path.Combine(dir, "comm"), comm + "\n");
        File.WriteAllText(Path.Combine(dir, "cmdline"), rawCmdline);
        return this;
    }

    public string AddDesktopDir(params (string FileName, string[] Lines)[] desktopFiles)
    {
        var dir = Path.Combine(Root, $"applications-{_nextDesktopDir++}");
        Directory.CreateDirectory(dir);
        foreach (var (fileName, lines) in desktopFiles)
            File.WriteAllLines(Path.Combine(dir, fileName), lines);
        return dir;
    }

    private string CreateEntry(string name)
    {
        var dir = Path.Combine(Root, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
