using System.Collections.Concurrent;
using System.Diagnostics;

namespace LoupixDeck.Utils;

public class CommandRunner : IDisposable
{
    private readonly BlockingCollection<string> _commandQueue = new BlockingCollection<string>();
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    private readonly Task _workerTask;

    public CommandRunner()
    {
        _workerTask = Task.Factory.StartNew(ProcessQueue, _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public void EnqueueCommand(string command)
    {
        _commandQueue.Add(command);
    }

    private void ProcessQueue()
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

    private void ExecuteCommand(string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{command}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process();
            process.StartInfo = psi;

            process.Start();
            
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            
            process.WaitForExit();

            if (!string.IsNullOrWhiteSpace(output))
                Console.WriteLine($"Output: {output}");

            if (!string.IsNullOrWhiteSpace(error))
                Console.WriteLine($"Error: {error}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception: {ex.Message}");
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