using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using LoupixDeck.Utils;

namespace LoupixDeck.Services.Macros;

/// <summary>
/// Records keyboard input on Linux by reading evdev events from the keyboard device
/// nodes under <c>/dev/input</c>. A background thread polls all detected keyboards and
/// raises a <see cref="IInputRecorder.KeyRecorded"/> event per press/release. Auto-repeat
/// events (value 2) are ignored.
///
/// Reading <c>/dev/input/event*</c> requires read permission (root or the <c>input</c>
/// group) — the same kind of access the uinput backend already needs. If no device can
/// be opened, recording simply yields nothing and a hint is logged.
/// </summary>
public sealed partial class LinuxInputRecorder : IInputRecorder
{
    private const int O_RDONLY = 0x0000;
    private const int O_NONBLOCK = 0x0800;
    private const short POLLIN = 0x0001;

    private const ushort EV_KEY = 0x01;
    private const int KeyRelease = 0;
    private const int KeyPress = 1;

    private static readonly int InputEventSize = Marshal.SizeOf<InputEvent>();

    private readonly Stopwatch _stopwatch = new();
    private readonly List<int> _fds = [];
    private Thread _thread;
    private volatile bool _running;
    private TimeSpan _lastEventAt;
    private bool _hasLastEvent;

    public bool IsSupported => true;
    public bool IsRecording { get; private set; }

    public event EventHandler<RecordedKeyEventArgs> KeyRecorded;

    public void Start()
    {
        if (IsRecording)
            return;

        OpenKeyboardDevices();
        if (_fds.Count == 0)
        {
            Console.Error.WriteLine(
                "[LinuxInputRecorder] No readable keyboard device found under /dev/input. " +
                "Recording needs read access (run as root or add the user to the 'input' group).");
            return;
        }

        _hasLastEvent = false;
        _lastEventAt = TimeSpan.Zero;
        _stopwatch.Restart();

        IsRecording = true;
        _running = true;
        _thread = new Thread(PollLoop)
        {
            IsBackground = true,
            Name = "LoupixDeck.InputRecorder"
        };
        _thread.Start();
    }

    public void Stop()
    {
        if (!IsRecording)
            return;

        IsRecording = false;
        _running = false;
        _thread?.Join(TimeSpan.FromSeconds(2));
        _thread = null;

        foreach (var fd in _fds)
            close(fd);
        _fds.Clear();

        _stopwatch.Stop();
    }

    private void OpenKeyboardDevices()
    {
        foreach (var node in DiscoverKeyboardNodes())
        {
            var fd = open(node, O_RDONLY | O_NONBLOCK);
            if (fd >= 0)
                _fds.Add(fd);
        }
    }

    /// <summary>
    /// Reads /proc/bus/input/devices and returns the /dev/input/eventN nodes of every
    /// device exposing the "kbd" handler (i.e. real keyboards).
    /// </summary>
    private static IEnumerable<string> DiscoverKeyboardNodes()
    {
        string content;
        try
        {
            content = File.ReadAllText("/proc/bus/input/devices");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[LinuxInputRecorder] Cannot read input device list: {ex.Message}");
            yield break;
        }

        // Blocks are separated by blank lines; the "H: Handlers=" line lists kbd + eventN.
        foreach (var block in content.Split("\n\n"))
        {
            var handlers = block.Split('\n')
                .FirstOrDefault(l => l.StartsWith("H: Handlers="));
            if (handlers == null || !handlers.Contains("kbd"))
                continue;

            var match = EventNodeRegex().Match(handlers);
            if (match.Success)
                yield return "/dev/input/" + match.Value;
        }
    }

    private void PollLoop()
    {
        var buffer = Marshal.AllocHGlobal(InputEventSize);
        try
        {
            var pollFds = _fds.Select(fd => new Pollfd { fd = fd, events = POLLIN }).ToArray();

            while (_running)
            {
                var ready = poll(pollFds, (nuint)pollFds.Length, 200);
                if (ready <= 0)
                    continue;

                foreach (var pfd in pollFds)
                {
                    if ((pfd.revents & POLLIN) != 0)
                        DrainDevice(pfd.fd, buffer);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void DrainDevice(int fd, IntPtr buffer)
    {
        // Non-blocking fd: read events until the queue is empty (read returns < size).
        while (_running)
        {
            var n = (long)read(fd, buffer, (IntPtr)InputEventSize);
            if (n < InputEventSize)
                break;

            var ev = Marshal.PtrToStructure<InputEvent>(buffer);
            if (ev.type != EV_KEY)
                continue;
            if (ev.value != KeyPress && ev.value != KeyRelease) // ignore autorepeat (2)
                continue;
            if (!KeyNames.TryGetLinuxName(ev.code, out var name))
                continue;

            var now = _stopwatch.Elapsed;
            var sinceLast = _hasLastEvent ? now - _lastEventAt : TimeSpan.Zero;
            _lastEventAt = now;
            _hasLastEvent = true;

            KeyRecorded?.Invoke(this, new RecordedKeyEventArgs(name, ev.value == KeyPress, sinceLast));
        }
    }

    [GeneratedRegex(@"event\d+")]
    private static partial Regex EventNodeRegex();

    // ───────── libc interop ─────────

    [StructLayout(LayoutKind.Sequential)]
    private struct InputEvent
    {
        public long tv_sec;
        public long tv_usec;
        public ushort type;
        public ushort code;
        public int value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Pollfd
    {
        public int fd;
        public short events;
        public short revents;
    }

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int open(string pathname, int flags);

    [DllImport("libc", EntryPoint = "read", SetLastError = true)]
    private static extern IntPtr read(int fd, IntPtr buf, IntPtr count);

    [DllImport("libc", EntryPoint = "poll", SetLastError = true)]
    private static extern int poll([In, Out] Pollfd[] fds, nuint nfds, int timeout);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int close(int fd);
}
