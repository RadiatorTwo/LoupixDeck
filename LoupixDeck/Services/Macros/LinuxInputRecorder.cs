using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using LoupixDeck.Native;
using LoupixDeck.Native.Types.Linux;
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
[SupportedOSPlatform("linux")]
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
    private ImmutableList<FileDescriptor> fds = [];
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
        if (fds.Count == 0)
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

        ImmutableList<FileDescriptor> fds = Interlocked.Exchange(ref this.fds, ImmutableList<FileDescriptor>.Empty);
        foreach (var fd in fds)
            fd.Close();

        _stopwatch.Stop();
    }

    private void OpenKeyboardDevices()
    {
        foreach (var node in DiscoverKeyboardNodes())
        {
            FileDescriptor? fd;
            try
            {
                fd = FileDescriptor.Open(node, FileAccess.Read, blocking: false);
            }
            catch (IOException)
            {
                fd = null;
            }
            ImmutableInterlocked.Update(ref fds, static (list, fd) => list.Add(fd), fd);
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

            var match = EventNodeRegex.Match(handlers);
            if (match.Success)
                yield return "/dev/input/" + match.Value;
        }
    }

    private unsafe void PollLoop()
    {
        var bufferPtr = Marshal.AllocHGlobal(InputEventSize);
        try
        {
            Span<byte> buffer = new(bufferPtr.ToPointer(), InputEventSize);
            ImmutableList<FileDescriptor> fds = this.fds;
            Dictionary<int, FileDescriptor> lookup = fds.ToDictionary(static fd => fd.Value);
            Pollfd[] pollFds = fds.Select(static fd => new Pollfd { fd = fd.Value, events = POLLIN }).ToArray();

            while (_running)
            {
                var ready = LibC.File.Poll(pollFds, 200);
                if (ready <= 0)
                    continue;

                foreach (var pfd in pollFds)
                {
                    if ((pfd.revents & POLLIN) != 0)
                        DrainDevice(lookup[pfd.fd], buffer);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(bufferPtr);
        }
    }

    private void DrainDevice(FileDescriptor fd, Span<byte> buffer)
    {
        // Non-blocking fd: read events until the queue is empty (read returns < size).
        while (_running)
        {
            if (!fd.TryRead(buffer, out var n) || n < InputEventSize)
                break;

            ref readonly InputEvent ev = ref MemoryMarshal.AsRef<InputEvent>(buffer);
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
    private static partial Regex EventNodeRegex { get; }
}
