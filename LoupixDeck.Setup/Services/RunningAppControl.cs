using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace LoupixDeck.Setup.Services;

/// <summary>
/// Coordinates with a running LoupixDeck instance so files can be replaced safely: a graceful
/// <c>quit</c> is sent over the app's named pipe (<c>LoupixDeck_Pipe</c>) which routes to
/// <c>QuitApplication()</c> for a clean device shutdown — preferred over killing the process.
/// </summary>
public static class RunningAppControl
{
    private const string PipeName = "LoupixDeck_Pipe";
    private const string ProcessName = "LoupixDeck"; // process image name without .exe

    /// <summary>Running LoupixDeck processes whose executable lives under <paramref name="installDir"/>.</summary>
    private static List<Process> ProcessesFor(string installDir)
    {
        List<Process> matches = new();
        string normalized = Path.GetFullPath(installDir).TrimEnd(Path.DirectorySeparatorChar);

        foreach (Process p in Process.GetProcessesByName(ProcessName))
        {
            try
            {
                string? exe = p.MainModule?.FileName;
                if (exe != null &&
                    Path.GetFullPath(exe).StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(p);
                    continue;
                }
            }
            catch
            {
                // Access denied / exited between enumeration and query — ignore.
            }

            p.Dispose();
        }

        return matches;
    }

    public static bool IsRunning(string installDir)
    {
        List<Process> procs = ProcessesFor(installDir);
        bool running = procs.Count > 0;
        foreach (Process p in procs)
            p.Dispose();
        return running;
    }

    /// <summary>Sends the graceful <c>quit</c> command over the app's named pipe (best effort).</summary>
    public static void RequestQuit()
    {
        try
        {
            using NamedPipeClientStream client = new(".", PipeName, PipeDirection.InOut);
            client.Connect(2000);
            byte[] bytes = Encoding.UTF8.GetBytes("quit");
            client.Write(bytes, 0, bytes.Length);
            if (OperatingSystem.IsWindows())
                client.WaitForPipeDrain();
        }
        catch
        {
            // No running instance / pipe unavailable — nothing to stop.
        }
    }

    /// <summary>
    /// Requests a graceful quit and waits up to <paramref name="timeout"/> for every matching
    /// process to exit. Returns true if none remain running.
    /// </summary>
    public static bool StopRunningApp(string installDir, TimeSpan timeout)
    {
        List<Process> procs = ProcessesFor(installDir);
        if (procs.Count == 0)
            return true;

        RequestQuit();

        DateTime deadline = DateTime.UtcNow + timeout;
        bool allExited = true;
        foreach (Process p in procs)
        {
            try
            {
                int remaining = (int)Math.Max(0, (deadline - DateTime.UtcNow).TotalMilliseconds);
                if (!p.WaitForExit(remaining))
                    allExited = false;
            }
            catch
            {
                // Treat inaccessible as exited.
            }
            finally
            {
                p.Dispose();
            }
        }

        return allExited;
    }

    /// <summary>Launches the installed application.</summary>
    public static void StartApp(string installDir)
    {
        string exe = Path.Combine(installDir, AppPaths.AppExeName);
        if (!File.Exists(exe))
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = installDir,
            UseShellExecute = true
        });
    }
}
