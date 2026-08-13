using System.Collections.Concurrent;
using System.Diagnostics;

namespace LoupixDeck.Services;

public interface ICommandRunner : IDisposable
{
    void EnqueueCommand(string command);
    void ProcessQueue();
    void ExecuteCommand(string command);
}

public class CommandRunner : ICommandRunner
{
    private readonly BlockingCollection<string> _commandQueue = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _workerTask;

    public CommandRunner()
    {
        _workerTask = Task.Factory.StartNew(ProcessQueue, _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public void EnqueueCommand(string command)
    {
        _commandQueue.Add(command);
    }

    public void ProcessQueue()
    {
        try
        {
            foreach (var command in _commandQueue.GetConsumingEnumerable(_cts.Token))
            {
                ExecuteCommand(command);
            }
        }
        catch (OperationCanceledException)
        {
            // We canceled processing the commands
        }
    }

    /// <summary>
    /// Working directory for spawned shell commands. Our own current directory is the installation
    /// folder — both the launcher and the start-menu shortcut set it — and a long-lived program
    /// started from here would inherit it and keep a handle on that folder open for its whole
    /// lifetime. On Windows that blocks the updater from replacing the installation, even after
    /// LoupixDeck itself has exited. The user's home directory is what a freshly opened shell would
    /// use anyway, and no install, update or uninstall path ever touches it. Consequence: shell
    /// commands must use absolute paths; relative ones resolve from the home directory.
    /// </summary>
    private static readonly string ShellWorkingDirectory = ResolveShellWorkingDirectory();

    private static string ResolveShellWorkingDirectory()
    {
        string home = Environment.GetEnvironmentVariable("HOME")
                      ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // An empty string leaves ProcessStartInfo at its default (inherit our own directory),
        // which is still better than failing to start the command at all.
        return !string.IsNullOrWhiteSpace(home) && Directory.Exists(home) ? home : string.Empty;
    }

    public void ExecuteCommand(string command)
    {
        Process process = null;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/bash",
                Arguments = OperatingSystem.IsWindows() ? $"/c \"{command}\"" : $"-c \"{command}\"",
                WorkingDirectory = ShellWorkingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            // Stream output/error asynchronously instead of ReadToEnd(): the worker must
            // not block, and reading both pipes sequentially could otherwise deadlock on a
            // chatty child. Same code path on Windows and Linux.
            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    Console.WriteLine($"Output: {e.Data}");
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    Console.WriteLine($"Error: {e.Data}");
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Fire-and-forget: do NOT WaitForExit() here. Launching a long-lived process
            // (e.g. `notepad`, a GUI app, a watcher) must not stall the queue — subsequent
            // shell commands have to run without first closing that window. The process
            // keeps running independently; we just dispose the wrapper once it exits.
            var launched = process;
            process = null; // ownership moves to the continuation; don't dispose in catch
            _ = launched.WaitForExitAsync().ContinueWith(_ =>
            {
                try { launched.Dispose(); }
                catch { /* already disposed */ }
            }, TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception: {ex.Message}");
            try { process?.Dispose(); }
            catch { /* ignore */ }
        }
    }

    public void Dispose()
    {
        _commandQueue.CompleteAdding();
        _cts.Cancel();

        try
        {
            _workerTask.Wait();
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException))
        {
            // Ignore Operation Cancel
        }

        _cts.Dispose();
        _commandQueue.Dispose();
    }
}