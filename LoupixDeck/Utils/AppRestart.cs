using System.Diagnostics;

namespace LoupixDeck.Utils;

/// <summary>
/// Restarts LoupixDeck: a change that only finishes on the next start should not force the user
/// to go and find the app themselves.
///
/// A naive relaunch does not work, because only one instance may run: the successor would reach
/// the single-instance gate (the Windows mutex, the Linux socket) while this process is still
/// dying and exit with "Already running." So the successor is told to wait for this process:
/// it is started with <see cref="WaitArgument"/> and this process's id, and waits at the very
/// top of Main until that id is gone - and, on Linux, until the socket file it leaves behind is
/// gone too - before taking the gate itself.
/// </summary>
public static class AppRestart
{
    /// <summary>Recognised in <c>Program.Main</c> before anything else looks at the arguments.</summary>
    public const string WaitArgument = "--restart-wait";

#if !WINDOWS
    private const string SocketPath = "/tmp/loupixdeck_app.sock";
#endif

    /// <summary>How long the successor waits before giving up and starting anyway. Generous:
    /// shutting every device down cleanly can take a moment, and starting anyway only risks the
    /// "Already running." message the user would have seen in any case.</summary>
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Starts the successor and returns true when it is on its way. The caller then shuts this
    /// instance down the ordinary way, so devices are stopped cleanly and the single-instance
    /// handle is released.
    /// </summary>
    public static bool BeginRestart()
    {
        string executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable))
        {
            Console.WriteLine("[Restart] The running executable could not be determined.");
            return false;
        }

        try
        {
            ProcessStartInfo start = new()
            {
                FileName = executable,
                UseShellExecute = false,
                // An installed copy may be started from anywhere; the successor must not
                // inherit a working directory it knows nothing about.
                WorkingDirectory = AppContext.BaseDirectory
            };
            start.ArgumentList.Add(WaitArgument);
            start.ArgumentList.Add(Environment.ProcessId.ToString());

            Process.Start(start);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Restart] Could not start '{executable}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Waits for the predecessor named in <paramref name="args"/> and returns the arguments with
    /// that instruction removed, so nothing downstream has to know about it. A normal start
    /// passes straight through.
    /// </summary>
    public static string[] WaitForPredecessor(string[] args)
    {
        if (args == null || args.Length == 0)
        {
            return args;
        }

        int index = Array.IndexOf(args, WaitArgument);
        if (index < 0)
        {
            return args;
        }

        if (index + 1 < args.Length && int.TryParse(args[index + 1], out int pid))
        {
            WaitForExit(pid);
        }

        // Drop the flag and its value; everything else is a real CLI argument.
        int drop = index + 1 < args.Length ? 2 : 1;
        return args.Take(index).Concat(args.Skip(index + drop)).ToArray();
    }

    private static void WaitForExit(int pid)
    {
        DateTime deadline = DateTime.UtcNow + WaitTimeout;

        try
        {
            using Process predecessor = Process.GetProcessById(pid);
            predecessor.WaitForExit(WaitTimeout);
        }
        catch (ArgumentException)
        {
            // Already gone - nothing to wait for.
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Restart] Waiting for process {pid} failed: {ex.Message}");
        }

#if !WINDOWS
        // The predecessor deletes the socket from its ProcessExit handler, which runs after the
        // process is observably finishing. Waiting for the file keeps the gate from being taken
        // twice.
        while (File.Exists(SocketPath) && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(100);
        }
#endif
    }
}
