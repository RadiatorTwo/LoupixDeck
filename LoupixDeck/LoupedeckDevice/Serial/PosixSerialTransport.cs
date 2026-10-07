#if !WINDOWS
using System.IO.Pipes;
using System.IO.Ports;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LoupixDeck.LoupedeckDevice.Serial;

/// <summary>
/// <see cref="ISerialTransport"/> for macOS and Linux that sleeps in <c>poll()</c> until the
/// device sends something.
/// </summary>
/// <remarks>
/// System.IO.Ports' Unix stream runs a background loop that polls the port with a 1 ms timeout
/// whenever a read is pending — and <see cref="SerialConnection"/>'s read thread always has one
/// pending — so an idle deck still costs ~1000 wakeups a second. Here the reading thread waits
/// in <c>poll()</c> on the port and on a wake pipe, with no timeout, and <see cref="Close"/>
/// writes to the pipe to release it.
///
/// Opening and configuring the port go through the same native helpers System.IO.Ports uses
/// (libSystem.IO.Ports.Native, shipped with the package), so the port ends up exactly as
/// <c>new SerialPort(name, baud) { 8N1, Handshake.None }.Open()</c> leaves it: exclusive, raw,
/// DTR and RTS cleared, and the non-standard 256000 baud applied the way each OS needs it.
/// </remarks>
internal sealed partial class PosixSerialTransport(string portName, int baudRate) : ISerialTransport
{
    private const string PortsNative = "libSystem.IO.Ports.Native";
    private const int WriteTimeoutMs = 3000;

    // Same values on macOS and Linux.
    private const short POLLIN = 0x01;
    private const short POLLOUT = 0x04;
    private const short POLLERR = 0x08;
    private const short POLLHUP = 0x10;
    private const short POLLNVAL = 0x20;

    private const int EINTR = 4;
    private static readonly int EAGAIN = OperatingSystem.IsMacOS() ? 35 : 11;

    // Interop.Termios.Signals in System.IO.Ports.
    private const int SignalDtr = 1 << 0;
    private const int SignalRts = 1 << 2;

    private SerialDeviceHandle _port;
    private AnonymousPipeServerStream _wakeWriter;
    private SafePipeHandle _wakeReader;
    private int _closed;

    public bool IsOpen => _port != null && Volatile.Read(ref _closed) == 0;

    public void Open()
    {
        nint fd = SerialPortOpen(portName);
        if (fd == -1)
            throw OpenError(Marshal.GetLastPInvokeError());

        var port = new SerialDeviceHandle(fd);
        try
        {
            if (TermiosReset(fd, baudRate, 8, (int)StopBits.One, (int)Parity.None, (int)Handshake.None) != 0)
                throw IoError(Marshal.GetLastPInvokeError(), $"Could not configure '{portName}'");

            // SerialPort clears both lines when DtrEnable/RtsEnable are left false, and ignores
            // drivers that have no modem lines to clear. Do the same.
            TermiosSetSignal(fd, SignalDtr, 0);
            TermiosSetSignal(fd, SignalRts, 0);

            _wakeWriter = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None);
            _wakeReader = _wakeWriter.ClientSafePipeHandle;
            _port = port;
        }
        catch
        {
            port.Dispose();
            _wakeWriter?.Dispose();
            _wakeReader?.Dispose();
            throw;
        }
    }

    public unsafe int Read(byte[] buffer, int offset, int count, int timeoutMs)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset + count, buffer.Length, nameof(count));
        if (count == 0)
            return 0;

        var port = _port;
        var wake = _wakeReader;
        if (port == null || wake == null)
            return 0;

        bool portRef = false, wakeRef = false;
        try
        {
            port.DangerousAddRef(ref portRef);
            wake.DangerousAddRef(ref wakeRef);
            int fd = (int)port.DangerousGetHandle();
            int wakeFd = (int)wake.DangerousGetHandle();

            long deadline = timeoutMs < 0 ? -1 : Environment.TickCount64 + timeoutMs;
            bool hungUp = false;

            fixed (byte* p = buffer)
            {
                while (Volatile.Read(ref _closed) == 0)
                {
                    int n = ReadNative(fd, p + offset, count);
                    if (n >= 0)
                        return n; // 0 = end of file: the device went away

                    int errno = Marshal.GetLastPInvokeError();
                    if (errno != EAGAIN)
                        throw IoError(errno, $"Read from '{portName}' failed");
                    if (hungUp)
                        throw new IOException($"'{portName}' was disconnected.");

                    short revents = Wait(fd, POLLIN, wakeFd, deadline, out bool woken);
                    if (woken)
                        return 0;
                    if (revents == 0)
                        throw new TimeoutException($"No data from '{portName}' within {timeoutMs} ms.");
                    if ((revents & POLLNVAL) != 0)
                        throw new IOException($"'{portName}' is no longer valid.");

                    // Readable, or an error/hang-up: the next read returns the data or the error.
                    // A hang-up that leaves nothing to read must not turn into a busy loop.
                    hungUp = (revents & POLLIN) == 0;
                }
            }

            return 0;
        }
        catch (ObjectDisposedException)
        {
            return 0; // closed before we got a reference
        }
        finally
        {
            if (portRef) port.DangerousRelease();
            if (wakeRef) wake.DangerousRelease();
        }
    }

    public unsafe void Write(byte[] buffer, int offset, int count)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset + count, buffer.Length, nameof(count));

        var port = _port;
        var wake = _wakeReader;
        if (port == null || wake == null)
            return;

        bool portRef = false, wakeRef = false;
        try
        {
            port.DangerousAddRef(ref portRef);
            wake.DangerousAddRef(ref wakeRef);
            int fd = (int)port.DangerousGetHandle();
            int wakeFd = (int)wake.DangerousGetHandle();

            long deadline = Environment.TickCount64 + WriteTimeoutMs;

            fixed (byte* p = buffer)
            {
                while (count > 0 && Volatile.Read(ref _closed) == 0)
                {
                    int n = WriteNative(fd, p + offset, count);
                    if (n > 0)
                    {
                        offset += n;
                        count -= n;
                        continue;
                    }

                    int errno = Marshal.GetLastPInvokeError();
                    if (n < 0 && errno != EAGAIN)
                        throw IoError(errno, $"Write to '{portName}' failed");

                    // The output queue is full; wait until the device has taken some of it.
                    short revents = Wait(fd, POLLOUT, wakeFd, deadline, out bool woken);
                    if (woken)
                        return;
                    if (revents == 0)
                        throw new TimeoutException($"Write to '{portName}' timed out.");
                    if ((revents & POLLOUT) == 0 && (revents & (POLLERR | POLLHUP | POLLNVAL)) != 0)
                        throw new IOException($"'{portName}' was disconnected.");
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // Closed before we got a reference; a write to a closed port is dropped.
        }
        finally
        {
            if (portRef) port.DangerousRelease();
            if (wakeRef) wake.DangerousRelease();
        }
    }

    public void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
            return;

        try
        {
            // Wakes a reader blocked in poll(). The pipe stays readable from here on, so any
            // later Read returns at once.
            _wakeWriter?.WriteByte(1);
        }
        catch
        {
            // Nothing is waiting on a pipe we cannot write to.
        }

        // Each handle really closes once the last Read/Write holding a reference releases it.
        try
        {
            _port?.Dispose();
            _wakeWriter?.Dispose();
            _wakeReader?.Dispose();
        }
        catch
        {
            // Already gone.
        }
    }

    /// <summary>
    /// Waits for <paramref name="events"/> on <paramref name="fd"/> or for <see cref="Close"/>.
    /// Returns the port's revents, 0 on timeout.
    /// </summary>
    private unsafe short Wait(int fd, short events, int wakeFd, long deadline, out bool woken)
    {
        PollFd* fds = stackalloc PollFd[2];
        while (true)
        {
            int timeout = deadline < 0 ? -1 : (int)Math.Max(0, deadline - Environment.TickCount64);

            fds[0] = new PollFd { Fd = fd, Events = events };
            fds[1] = new PollFd { Fd = wakeFd, Events = POLLIN };

            int ready = Poll(fds, 2, timeout);
            if (ready < 0)
            {
                int errno = Marshal.GetLastPInvokeError();
                if (errno == EINTR)
                    continue;
                throw IoError(errno, $"Waiting on '{portName}' failed");
            }

            woken = fds[1].Revents != 0;
            return ready == 0 ? (short)0 : fds[0].Revents;
        }
    }

    /// <summary>The exception types <see cref="SerialPort.Open"/> raises for the same errno.</summary>
    private Exception OpenError(int errno) => errno switch
    {
        2 /* ENOENT */ => new FileNotFoundException($"Could not find serial port '{portName}'.", portName),
        1 /* EPERM */ or 9 /* EBADF */ or 13 /* EACCES */ =>
            new UnauthorizedAccessException($"Access to the port '{portName}' is denied."),
        _ => IoError(errno, $"Could not open '{portName}'")
    };

    private static IOException IoError(int errno, string what) =>
        new($"{what}: {Marshal.GetPInvokeErrorMessage(errno)}", errno);

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int Fd;
        public short Events;
        public short Revents;
    }

    /// <summary>Releases the exclusive lock and closes the descriptor, as SerialPort does.</summary>
    private sealed class SerialDeviceHandle : SafeHandleMinusOneIsInvalid
    {
        public SerialDeviceHandle(nint fd) : base(ownsHandle: true) => SetHandle(fd);

        protected override bool ReleaseHandle() => SerialPortClose(handle) == 0;
    }

    [LibraryImport(PortsNative, EntryPoint = "SystemIoPortsNative_SerialPortOpen",
        StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial nint SerialPortOpen(string name);

    [LibraryImport(PortsNative, EntryPoint = "SystemIoPortsNative_SerialPortClose", SetLastError = true)]
    private static partial int SerialPortClose(nint fd);

    [LibraryImport(PortsNative, EntryPoint = "SystemIoPortsNative_TermiosReset", SetLastError = true)]
    private static partial int TermiosReset(nint fd, int speed, int dataBits, int stopBits, int parity, int handshake);

    [LibraryImport(PortsNative, EntryPoint = "SystemIoPortsNative_TermiosSetSignal", SetLastError = true)]
    private static partial int TermiosSetSignal(nint fd, int signal, int set);

    [LibraryImport(PortsNative, EntryPoint = "SystemIoPortsNative_Read", SetLastError = true)]
    private static unsafe partial int ReadNative(nint fd, byte* buffer, int count);

    [LibraryImport(PortsNative, EntryPoint = "SystemIoPortsNative_Write", SetLastError = true)]
    private static unsafe partial int WriteNative(nint fd, byte* buffer, int count);

    [LibraryImport("libc", EntryPoint = "poll", SetLastError = true)]
    private static unsafe partial int Poll(PollFd* fds, uint nfds, int timeout);
}
#endif
