using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace LoupixDeck.Services.SystemPower.Linux;

/// <summary>
/// X11 sessions outside GNOME: the DPMS extension's <c>InfoNotify</c> event, added in DPMS 1.2
/// (Xorg 21.1). libXext has no wrapper for selecting it, so the three requests involved are sent
/// raw through libxcb, which every X11 session has. A dedicated thread blocks in
/// <c>xcb_wait_for_event</c>; a server older than DPMS 1.2 is left unsupported rather than polled.
/// </summary>
public sealed unsafe partial class X11DpmsSource : ILinuxDisplaySource
{
    private const string Xcb = "libxcb.so.1";

    // DPMS minor opcodes (dpmsproto.h).
    private const byte DpmsGetVersion = 0;
    private const byte DpmsInfo = 7;
    private const byte DpmsSelectInput = 8;
    private const uint DpmsInfoNotifyMask = 1;
    private const ushort DpmsInfoNotify = 0;
    private const ushort DpmsModeOn = 0;

    private const byte XcbGeGeneric = 35;

    private XcbConnectionHandle _connection;
    private byte _majorOpcode;
    private Thread _reader;
    private Action<bool> _report;

    public string Name => "X11 DPMS";

    public Task<bool> TryStartAsync(Action<bool> report)
    {
        _connection = xcb_connect(null, out _);
        if (_connection.IsInvalid || xcb_connection_has_error(_connection) != 0) return Task.FromResult(false);

        if (!QueryDpmsOpcode()) return Task.FromResult(false);

        // GetVersion: client major/minor, reply carries the server's at offsets 8/10.
        byte[] reply = Request([_majorOpcode, DpmsGetVersion, 0, 0, 1, 0, 2, 0]);
        if (reply == null) return Task.FromResult(false);
        ushort major = BinaryPrimitives.ReadUInt16LittleEndian(reply.AsSpan(8));
        ushort minor = BinaryPrimitives.ReadUInt16LittleEndian(reply.AsSpan(10));
        if (major < 1 || (major == 1 && minor < 2))
        {
            Console.WriteLine($"[Displays] X server offers DPMS {major}.{minor}; change events need 1.2.");
            return Task.FromResult(false);
        }

        _report = report;

        // Select first, then read the current state, so no change can fall between the two.
        byte[] select = [_majorOpcode, DpmsSelectInput, 0, 0, 0, 0, 0, 0];
        BinaryPrimitives.WriteUInt32LittleEndian(select.AsSpan(4), DpmsInfoNotifyMask);
        SendVoid(select);

        byte[] info = Request([_majorOpcode, DpmsInfo, 0, 0]);
        if (info == null) return Task.FromResult(false);
        report(IsOn(BinaryPrimitives.ReadUInt16LittleEndian(info.AsSpan(8)), info[10] != 0));

        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "X11 display state" };
        _reader.Start();
        return Task.FromResult(true);
    }

    /// <summary>DPMS disabled means the server never blanks the monitors.</summary>
    private static bool IsOn(ushort powerLevel, bool enabled) => !enabled || powerLevel == DpmsModeOn;

    private bool QueryDpmsOpcode()
    {
        uint cookie = xcb_query_extension(_connection, 4, "DPMS");
        IntPtr reply = xcb_query_extension_reply(_connection, cookie, IntPtr.Zero);
        if (reply == IntPtr.Zero) return false;
        try
        {
            // xcb_query_extension_reply_t: present at offset 8, major_opcode at 9.
            byte* bytes = (byte*)reply;
            if (bytes[8] == 0) return false;
            _majorOpcode = bytes[9];
            return true;
        }
        finally
        {
            free(reply);
        }
    }

    private void ReadLoop()
    {
        XcbConnectionHandle connection = _connection;
        try
        {
            while (true)
            {
                IntPtr ev = xcb_wait_for_event(connection);
                // Null: the connection broke or was shut down.
                if (ev == IntPtr.Zero) return;
                try
                {
                    // xcb_ge_generic_event_t: extension at 1, event_type at 8; the DPMS payload
                    // continues with timestamp (12), power_level (16) and state (18).
                    byte* bytes = (byte*)ev;
                    if ((bytes[0] & 0x7f) == XcbGeGeneric && bytes[1] == _majorOpcode &&
                        BinaryPrimitives.ReadUInt16LittleEndian(new ReadOnlySpan<byte>(bytes + 8, 2)) == DpmsInfoNotify)
                    {
                        ushort level = BinaryPrimitives.ReadUInt16LittleEndian(new ReadOnlySpan<byte>(bytes + 16, 2));
                        _report?.Invoke(IsOn(level, bytes[18] != 0));
                    }
                }
                finally
                {
                    free(ev);
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // Disposed while waiting.
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Displays] X11 display state reader stopped: {ex.Message}");
        }
    }

    /// <summary>Sends a request that has a reply and returns the reply bytes, or null on error.</summary>
    private byte[] Request(byte[] request)
    {
        uint sequence = Send(request, isVoid: false);
        IntPtr reply = xcb_wait_for_reply(_connection, sequence, out IntPtr error);
        if (error != IntPtr.Zero) free(error);
        if (reply == IntPtr.Zero) return null;
        try
        {
            // Every reply is 32 bytes plus 4 * length (offset 4).
            byte* bytes = (byte*)reply;
            int size = 32 + (4 * (int)BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(bytes + 4, 4)));
            return new ReadOnlySpan<byte>(bytes, size).ToArray();
        }
        finally
        {
            free(reply);
        }
    }

    private void SendVoid(byte[] request)
    {
        Send(request, isVoid: true);
        xcb_flush(_connection);
    }

    /// <summary>
    /// Raw <c>xcb_send_request</c> with no extension descriptor: libxcb then writes the given
    /// opcode (our DPMS major opcode) into byte 0 and fills in the length, while byte 1 already
    /// holds the DPMS minor opcode. The vector needs two spare iovecs in front for libxcb.
    /// </summary>
    private uint Send(byte[] request, bool isVoid)
    {
        fixed (byte* data = request)
        {
            IoVec* vector = stackalloc IoVec[3];
            vector[2] = new IoVec { Base = (IntPtr)data, Length = (nuint)request.Length };
            ProtocolRequest protocol = new() { Count = 1, Extension = IntPtr.Zero, Opcode = request[0], IsVoid = (byte)(isVoid ? 1 : 0) };
            // XCB_REQUEST_CHECKED routes an error of a request with a reply to that reply, as the
            // generated xcb wrappers do; an unchecked void request's error lands in the event queue,
            // where the reader ignores it.
            return xcb_send_request(_connection, isVoid ? 0 : 1, vector + 2, &protocol);
        }
    }

    public void Dispose()
    {
        _report = null;
        // A handle in use by the blocked reader is only released once that call returns.
        _connection?.Dispose();
        _connection = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoVec
    {
        public IntPtr Base;
        public nuint Length;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProtocolRequest
    {
        public nuint Count;
        public IntPtr Extension;
        public byte Opcode;
        public byte IsVoid;
    }

    /// <summary>Owns an <c>xcb_connection_t*</c> and disconnects on release.</summary>
    private sealed class XcbConnectionHandle() : Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid(true)
    {
        protected override bool ReleaseHandle()
        {
            xcb_disconnect(handle);
            return true;
        }
    }

    [LibraryImport(Xcb, StringMarshalling = StringMarshalling.Utf8)]
    private static partial XcbConnectionHandle xcb_connect(string displayName, out int screen);

    [LibraryImport(Xcb)]
    private static partial void xcb_disconnect(IntPtr connection);

    [LibraryImport(Xcb)]
    private static partial int xcb_connection_has_error(XcbConnectionHandle connection);

    [LibraryImport(Xcb)]
    private static partial int xcb_flush(XcbConnectionHandle connection);

    [LibraryImport(Xcb, StringMarshalling = StringMarshalling.Utf8)]
    private static partial uint xcb_query_extension(XcbConnectionHandle connection, ushort nameLength, string name);

    [LibraryImport(Xcb)]
    private static partial IntPtr xcb_query_extension_reply(XcbConnectionHandle connection, uint cookie, IntPtr error);

    [LibraryImport(Xcb)]
    private static partial uint xcb_send_request(XcbConnectionHandle connection, int flags, IoVec* vector, ProtocolRequest* request);

    [LibraryImport(Xcb)]
    private static partial IntPtr xcb_wait_for_reply(XcbConnectionHandle connection, uint sequence, out IntPtr error);

    [LibraryImport(Xcb)]
    private static partial IntPtr xcb_wait_for_event(XcbConnectionHandle connection);

    [LibraryImport("libc")]
    private static partial void free(IntPtr pointer);
}
