using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace LoupixDeck.Services.ProcessWatch;

/// <summary>
/// Linux: one <c>pidfd_open</c> descriptor per watched process, all waited on by a single
/// background thread blocked in <c>poll</c> with no timeout. A pidfd becomes readable when its
/// process exits, so the thread wakes only for an exit or for a change of the watch list, which an
/// <c>eventfd</c> signals.
/// </summary>
/// <remarks>
/// <c>pidfd_open</c> needs kernel 5.3. On an older kernel <see cref="Watch"/> returns null and the
/// caller learns about the exit at its next one-shot lookup instead.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed partial class PidfdExitNotifier : IExitNotifier
{
    // The same number on every architecture: pidfd_open came after the syscall tables were unified.
    private const long SysPidfdOpen = 434;
    private const short PollIn = 0x001;
    private const int EfdCloexec = 0x80000;
    private const int Esrch = 3;
    private const int Eintr = 4;

    private readonly Lock _gate = new();
    private readonly Dictionary<int, Entry> _entries = new();
    private readonly List<Entry> _pendingClose = [];
    private readonly FileDescriptorHandle _wakeup;
    private readonly Thread _thread;
    private bool _stopping;
    private int _nextId;

    public PidfdExitNotifier()
    {
        int wakeup = EventFd(0, EfdCloexec);
        if (wakeup < 0)
            throw new IOException($"eventfd failed (errno {Marshal.GetLastPInvokeError()})");

        _wakeup = new FileDescriptorHandle(wakeup);
        _thread = new Thread(Run) { IsBackground = true, Name = "ProcessExitWatch" };
        _thread.Start();
    }

    public IDisposable Watch(int pid, Action onExit)
    {
        long fd = Syscall(SysPidfdOpen, pid, 0);
        if (fd < 0)
        {
            int errno = Marshal.GetLastPInvokeError();
            if (errno == Esrch)
            {
                // Already gone.
                onExit();
            }

            return null;
        }

        Entry entry = new(new FileDescriptorHandle((int)fd), onExit);
        lock (_gate)
        {
            if (_stopping)
            {
                entry.Handle.Dispose();
                return null;
            }

            int id = ++_nextId;
            _entries[id] = entry;
            entry.Id = id;
        }

        Wake();
        return new Subscription(this, entry);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_stopping)
                return;
            _stopping = true;
        }

        Wake();
        _thread.Join(TimeSpan.FromSeconds(2));
    }

    private void Remove(Entry entry)
    {
        lock (_gate)
        {
            if (_stopping || !_entries.Remove(entry.Id))
                return;

            // The loop thread owns the descriptors it polls; it closes this one on its next pass,
            // while no poll is using it.
            _pendingClose.Add(entry);
        }

        Wake();
    }

    private void Wake()
    {
        bool added = false;
        try
        {
            // The AddRef keeps the descriptor open until the write is done, so it can never hit a
            // closed and reused descriptor number.
            _wakeup.DangerousAddRef(ref added);
            ulong one = 1;
            Write(_wakeup.Fd, ref one, sizeof(ulong));
        }
        catch (ObjectDisposedException)
        {
            // The loop has stopped; nothing to wake.
        }
        finally
        {
            if (added)
                _wakeup.DangerousRelease();
        }
    }

    private void Run()
    {
        List<Entry> polled = [];
        while (true)
        {
            List<Entry> closeNow;
            lock (_gate)
            {
                if (_stopping)
                    break;

                closeNow = [.. _pendingClose];
                _pendingClose.Clear();
                polled = _entries.Values.ToList();
            }

            foreach (Entry entry in closeNow)
                entry.Handle.Dispose();

            PollFd[] fds = new PollFd[polled.Count + 1];
            fds[0] = new PollFd { Fd = _wakeup.Fd, Events = PollIn };
            for (int i = 0; i < polled.Count; i++)
                fds[i + 1] = new PollFd { Fd = polled[i].Handle.Fd, Events = PollIn };

            int ready = Poll(fds, (nuint)fds.Length, -1);
            if (ready < 0)
            {
                int errno = Marshal.GetLastPInvokeError();
                if (errno == Eintr)
                    continue;

                Console.WriteLine($"[ProcessWatch] poll failed (errno {errno}); exit watch stopped.");
                break;
            }

            if ((fds[0].Revents & PollIn) != 0)
                Read(_wakeup.Fd, out _, sizeof(ulong));

            for (int i = 0; i < polled.Count; i++)
            {
                if (fds[i + 1].Revents == 0)
                    continue;

                Entry entry = polled[i];
                bool fire;
                lock (_gate)
                    fire = _entries.Remove(entry.Id);

                entry.Handle.Dispose();
                if (fire)
                    Fire(entry.OnExit);
            }
        }

        lock (_gate)
        {
            // Also when poll failed: no Watch or Remove may touch the wakeup descriptor after this.
            _stopping = true;
            foreach (Entry entry in _entries.Values.Concat(_pendingClose))
                entry.Handle.Dispose();
            _entries.Clear();
            _pendingClose.Clear();
        }

        _wakeup.Dispose();
    }

    private static void Fire(Action onExit)
    {
        try
        {
            onExit();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ProcessWatch] Exit handler failed: {ex.Message}");
        }
    }

    private sealed class Entry(FileDescriptorHandle handle, Action onExit)
    {
        public int Id;
        public FileDescriptorHandle Handle { get; } = handle;
        public Action OnExit { get; } = onExit;
    }

    private sealed class Subscription(PidfdExitNotifier owner, Entry entry) : IDisposable
    {
        public void Dispose() => owner.Remove(entry);
    }

    /// <summary>Owns a Unix file descriptor and closes it once.</summary>
    private sealed class FileDescriptorHandle : SafeHandleMinusOneIsInvalid
    {
        public FileDescriptorHandle(int fd) : base(ownsHandle: true) => SetHandle(fd);

        public int Fd => (int)handle;

        protected override bool ReleaseHandle() => CloseFd((int)handle) == 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int Fd;
        public short Events;
        public short Revents;
    }

    [LibraryImport("libc", EntryPoint = "syscall", SetLastError = true)]
    private static partial long Syscall(long number, int pid, uint flags);

    [LibraryImport("libc", EntryPoint = "eventfd", SetLastError = true)]
    private static partial int EventFd(uint initialValue, int flags);

    [LibraryImport("libc", EntryPoint = "poll", SetLastError = true)]
    private static partial int Poll([In, Out] PollFd[] fds, nuint count, int timeout);

    [LibraryImport("libc", EntryPoint = "read", SetLastError = true)]
    private static partial nint Read(int fd, out ulong value, nint count);

    [LibraryImport("libc", EntryPoint = "write", SetLastError = true)]
    private static partial nint Write(int fd, ref ulong value, nint count);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    private static partial int CloseFd(int fd);
}
