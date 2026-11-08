using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace LoupixDeck.Services.ProcessWatch;

/// <summary>
/// Windows: <see cref="Process.Exited"/>, which .NET implements as a thread-pool wait on the
/// process handle — the kernel signals it, nothing polls.
/// </summary>
/// <remarks>
/// Not used on Unix: there .NET can wait only for its own child processes and checks any other
/// process periodically, which is exactly what the watcher must not do.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class ProcessHandleExitNotifier : IExitNotifier
{
    public IDisposable Watch(int pid, Action onExit)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            // Gone before it could be opened.
            onExit();
            return null;
        }

        try
        {
            Subscription subscription = new(process, onExit);
            process.Exited += subscription.OnExited;
            process.EnableRaisingEvents = true;
            if (process.HasExited)
                subscription.OnExited(process, EventArgs.Empty);
            return subscription;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // A process of another user or a protected one cannot be opened for waiting.
            process.Dispose();
            return null;
        }
    }

    public void Dispose()
    {
    }

    private sealed class Subscription(Process process, Action onExit) : IDisposable
    {
        private int _done;

        public void OnExited(object sender, EventArgs e)
        {
            if (Interlocked.Exchange(ref _done, 1) == 0)
                onExit();
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _done, 1);
            process.Exited -= OnExited;
            process.Dispose();
        }
    }
}
