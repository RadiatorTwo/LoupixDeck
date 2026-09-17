using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace LoupixDeck.Services.Diagnostics.Linux;

/// <summary>
/// The libc entry points and uinput constants the diagnostics need.
///
/// UInputKeyboard and UInputMouse declare their own private copies of these. Folding all three
/// onto one shared class is worth doing, but it rewrites macro playback and the virtual mouse -
/// the highest-risk, test-free part of the app - so it does not belong in a feature change.
/// TODO(#258 follow-up): fold this and the two input classes into Services/Interop/LinuxInput.cs
/// in a behaviour-preserving commit of its own.
/// </summary>
internal static partial class LinuxInputInterop
{
    public const string UinputPath = "/dev/uinput";

    public const int O_RDONLY = 0x0000;
    public const int O_WRONLY = 0x0001;
    public const int O_NONBLOCK = 0x0800;

    public const int EPERM = 1;
    public const int ENOENT = 2;
    public const int EACCES = 13;

    public const int UI_SET_EVBIT = 0x40045564;
    public const int UI_SET_KEYBIT = 0x40045565;
    public const int UI_DEV_CREATE = 0x5501;
    public const int UI_DEV_DESTROY = 0x5502;

    public const int EV_KEY = 0x01;

    /// <summary>F24. Nothing binds it, so an accidental event could not trigger anything.</summary>
    public const int KEY_F24 = 194;

    // uinput_user_dev: name[80] + input_id (8) + ff_effects_max (4) + 4 * abs[64].
    private const int UinputMaxNameSize = 80;
    private const int AbsCnt = 64;
    private const int UinputUserDevSize = UinputMaxNameSize + 8 + 4 + (AbsCnt * 4 * 4);

    [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int open(string pathname, int flags);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    public static partial int close(int fd);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    public static partial int ioctl(int fd, int request, int value);

    [LibraryImport("libc", EntryPoint = "write", SetLastError = true)]
    public static partial nint write(int fd, ReadOnlySpan<byte> buffer, nuint count);

    [LibraryImport("libc", EntryPoint = "getgroups", SetLastError = true)]
    public static partial int getgroups(int size, [Out] uint[] list);

    /// <summary>
    /// Opens a path and closes it again straight away. This is the permission probe the
    /// diagnostics are built on: opening /dev/uinput allocates a kernel uinput instance but
    /// creates no input device, and opening an event node read-only does not grab it, so
    /// neither probe has an effect anybody can observe.
    /// </summary>
    /// <returns>0 on success, otherwise the errno of the failed open.</returns>
    public static int TryOpenAndClose(string path, int flags)
    {
        int fileDescriptor = open(path, flags);

        if (fileDescriptor < 0)
        {
            return Marshal.GetLastPInvokeError();
        }

        close(fileDescriptor);
        return 0;
    }

    /// <summary>The supplementary group ids of the running process, or null when unreadable.</summary>
    public static uint[] EffectiveGroups()
    {
        int count = getgroups(0, []);

        if (count < 0)
        {
            return null;
        }

        uint[] groups = new uint[count];

        return getgroups(count, groups) < 0 ? null : groups;
    }

    /// <summary>
    /// Writes the uinput_user_dev struct that has to precede UI_DEV_CREATE. The device carries
    /// no capabilities beyond the single key bit the caller set.
    /// </summary>
    public static bool WriteUserDev(int fileDescriptor, string name)
    {
        Span<byte> buffer = stackalloc byte[UinputUserDevSize];
        buffer.Clear();
        Encoding.ASCII.GetBytes(name, buffer[..(UinputMaxNameSize - 1)]);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[82..], 0x0001);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[84..], 0x0001);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[86..], 1);

        return write(fileDescriptor, buffer, (nuint)UinputUserDevSize) == UinputUserDevSize;
    }
}
