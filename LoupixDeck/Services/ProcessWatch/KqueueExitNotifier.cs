using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace LoupixDeck.Services.ProcessWatch;

/// <summary>
/// macOS: an <c>EVFILT_PROC</c>/<c>NOTE_EXIT</c> filter per watched process on one kqueue, read
/// by a background thread blocked in <c>kevent</c> with no timeout. An <c>EVFILT_USER</c> event
/// wakes the thread to stop.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed partial class KqueueExitNotifier : IExitNotifier
{
    private const short EvfiltProc = -5;
    private const short EvfiltUser = -10;
    private const ushort EvAdd = 0x0001;
    private const ushort EvDelete = 0x0002;
    private const ushort EvOneshot = 0x0010;
    private const ushort EvClear = 0x0020;
    private const ushort EvError = 0x4000;
    private const uint NoteExit = 0x80000000;
    private const uint NoteTrigger = 0x01000000;
    private const nuint StopIdent = 1;
    private const int Esrch = 3;
    private const int Eintr = 4;

    private readonly Lock _gate = new();
    private readonly Dictionary<int, List<Subscription>> _watches = new();
    private readonly KqueueHandle _queue;
    private readonly Thread _thread;
    private bool _stopping;

    public KqueueExitNotifier()
    {
        int queue = Kqueue();
        if (queue < 0)
            throw new IOException($"kqueue failed (errno {Marshal.GetLastPInvokeError()})");

        _queue = new KqueueHandle(queue);
        if (Change(new KEvent { Ident = StopIdent, Filter = EvfiltUser, Flags = EvAdd | EvClear }) != 0)
        {
            int errno = Marshal.GetLastPInvokeError();
            _queue.Dispose();
            throw new IOException($"kevent EVFILT_USER failed (errno {errno})");
        }

        _thread = new Thread(Run) { IsBackground = true, Name = "ProcessExitWatch" };
        _thread.Start();
    }

    public IDisposable Watch(int pid, Action onExit)
    {
        Subscription subscription = new(this, pid, onExit);
        lock (_gate)
        {
            if (_stopping)
                return null;

            if (_watches.TryGetValue(pid, out List<Subscription> list))
            {
                list.Add(subscription);
                return subscription;
            }

            KEvent change = new()
            {
                Ident = (nuint)pid,
                Filter = EvfiltProc,
                Flags = EvAdd | EvOneshot,
                Fflags = NoteExit
            };

            if (Change(change) != 0)
            {
                int errno = Marshal.GetLastPInvokeError();
                if (errno != Esrch)
                    return null;

                // Already gone; report it outside the lock.
                subscription = null;
            }
            else
            {
                _watches[pid] = [subscription];
            }
        }

        if (subscription == null)
        {
            Fire(onExit);
            return null;
        }

        return subscription;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_stopping)
                return;
            _stopping = true;
            Change(new KEvent { Ident = StopIdent, Filter = EvfiltUser, Fflags = NoteTrigger });
        }

        _thread.Join(TimeSpan.FromSeconds(2));
    }

    private void Remove(Subscription subscription)
    {
        lock (_gate)
        {
            if (_stopping || !_watches.TryGetValue(subscription.Pid, out List<Subscription> list))
                return;

            list.Remove(subscription);
            if (list.Count > 0)
                return;

            _watches.Remove(subscription.Pid);
            Change(new KEvent { Ident = (nuint)subscription.Pid, Filter = EvfiltProc, Flags = EvDelete });
        }
    }

    private void Run()
    {
        KEvent[] events = new KEvent[16];
        while (true)
        {
            int count = Wait(events);
            if (count < 0)
            {
                int errno = Marshal.GetLastPInvokeError();
                if (errno == Eintr)
                    continue;

                Console.WriteLine($"[ProcessWatch] kevent failed (errno {errno}); exit watch stopped.");
                break;
            }

            bool stop = false;
            List<Subscription> exited = [];
            lock (_gate)
            {
                for (int i = 0; i < count; i++)
                {
                    if (events[i].Filter == EvfiltUser)
                    {
                        stop = true;
                        continue;
                    }

                    if (events[i].Filter != EvfiltProc || (events[i].Flags & EvError) != 0)
                        continue;

                    int pid = (int)events[i].Ident;
                    if (_watches.Remove(pid, out List<Subscription> list))
                        exited.AddRange(list);
                }
            }

            foreach (Subscription subscription in exited)
                Fire(subscription.OnExit);

            if (stop)
                break;
        }

        lock (_gate)
        {
            _stopping = true;
            _watches.Clear();
            _queue.Dispose();
        }
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

    /// <summary>Applies one change to the queue. Callers hold <see cref="_gate"/> or own the queue.</summary>
    private int Change(KEvent change)
    {
        KEvent[] changes = [change];
        return KEventCall(_queue, changes, 1, null, 0, IntPtr.Zero) < 0 ? -1 : 0;
    }

    private int Wait(KEvent[] events) => KEventCall(_queue, null, 0, events, events.Length, IntPtr.Zero);

    private sealed class Subscription(KqueueExitNotifier owner, int pid, Action onExit) : IDisposable
    {
        public int Pid { get; } = pid;
        public Action OnExit { get; } = onExit;

        public void Dispose() => owner.Remove(this);
    }

    private sealed class KqueueHandle : SafeHandleMinusOneIsInvalid
    {
        public KqueueHandle(int fd) : base(ownsHandle: true) => SetHandle(fd);

        protected override bool ReleaseHandle() => CloseFd((int)handle) == 0;
    }

    /// <summary><c>struct kevent</c> of the 64-bit Darwin ABI (32 bytes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct KEvent
    {
        public nuint Ident;
        public short Filter;
        public ushort Flags;
        public uint Fflags;
        public nint Data;
        public nint Udata;
    }

    [LibraryImport("libc", EntryPoint = "kqueue", SetLastError = true)]
    private static partial int Kqueue();

    [LibraryImport("libc", EntryPoint = "kevent", SetLastError = true)]
    private static partial int KEventCall(KqueueHandle kq, [In] KEvent[] changes, int changeCount,
        [Out] KEvent[] events, int eventCount, IntPtr timeout);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    private static partial int CloseFd(int fd);
}
