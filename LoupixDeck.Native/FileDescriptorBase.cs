using Microsoft.Win32.SafeHandles;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LoupixDeck.Native;

public abstract class FileDescriptorBase : SafeHandleMinusOneIsInvalid
{
    protected FileDescriptorBase(string path, FileAccess flags, bool blocking) : this(OpenBare(path, flags, blocking)) { }

    protected FileDescriptorBase(int fd) : base(true)
    {
        if (fd < 0)
            throw new ArgumentException($"Invalid file descriptor: {fd}", nameof(fd));
        SetHandle(fd);
    }

    protected static int OpenBare(string path, FileAccess flags, bool blocking)
    {
        int lFlags = LibC.File.Constants.MapFlags(flags);
        int blockingFlag = blocking ? 0 : LibC.File.Constants.O_NONBLOCK;
        int fd = LibC.File.Open(path, lFlags | blockingFlag);
        if (fd < 0)
            throw new IOException($"Could not open {path}. Is it present and are the permissions set?");
        return fd;
    }

    protected int IoControl(int request) => IoControl(request, 0);
    protected int IoControl(int request, int arg)
    {
        int returnCode = LibC.File.ioctl((int)handle, request, arg);
        Debug.WriteLineIf(returnCode is not 0, $"ioctl failed with request {request} and arg {arg}: {Marshal.GetLastWin32Error()}");
        return returnCode;
    }

    protected void Write(ReadOnlySpan<byte> buffer)
    {
        bool success = TryWrite(buffer, out long bytesWritten);
        if (!success || bytesWritten != buffer.Length)
            throw new IOException($"Failed to write input event to /dev/uinput. Written bytes: {bytesWritten}");
    }

    protected bool TryWrite(ReadOnlySpan<byte> buffer, out long bytesWritten)
    {
        IntPtr resultCode = LibC.File.Write(handle.ToInt32(), buffer);
        if (resultCode > 0)
        {
            bytesWritten = resultCode.ToInt64();
            return true;
        }
        bytesWritten = 0;
        return false;
    }

    protected override bool ReleaseHandle()
    {
        try
        {
            LibC.File.Close(handle.ToInt32());
        }
        catch { /* File may be gone — nothing to clean up */ }
        return true;
    }
}
