using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using LoupixDeck.Native.Types.Linux;

namespace LoupixDeck.Native;

public static partial class LibC
{
    public static partial class File
    {
        public static class Constants
        {
            public const int O_RDONLY = 0x0000;
            public const int O_WRONLY = 0x0001;
            public const int O_RDWR = 0x0002;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static int MapFlags(FileAccess access) => access switch
            {
                FileAccess.Read => O_RDONLY,
                FileAccess.Write => O_WRONLY,
                FileAccess.ReadWrite => O_RDWR,
                _ => throw new ArgumentOutOfRangeException(nameof(access), "Invalid FileAccess value")
            };

            public const int O_NONBLOCK = 0x0800;
        }

        // TODO: validate StringMarshalling should be Utf8, and not StringMarshalling.Utf16
        [LibraryImport(LibraryName, EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
        public static partial int Open(string pathname, int flags);

        [LibraryImport(LibraryName, EntryPoint = "ioctl", SetLastError = true)]
        public static partial int ioctl(int fd, int request, int value);

        [LibraryImport(LibraryName, EntryPoint = "write", SetLastError = true)]
        public static partial IntPtr Write(int fd, ReadOnlySpan<byte> buffer, UIntPtr count);
        public static IntPtr Write(int fd, ReadOnlySpan<byte> buffer) => Write(fd, buffer, (UIntPtr)buffer.Length);

        [LibraryImport(LibraryName, EntryPoint = "read", SetLastError = true)]
        private static partial IntPtr Read(int fd, Span<byte> buf, UIntPtr count);e
        public static IntPtr Read(int fd, Span<byte> buffer) => Read(fd, buffer, (UIntPtr)buffer.Length);

        [LibraryImport(LibraryName, EntryPoint = "poll", SetLastError = true)]
        private static partial int Poll(Span<Pollfd> fds, nuint nfds, int timeoutMillis);

        public static int Poll(Span<Pollfd> fds, TimeSpan timeout) => Poll(fds, (int)timeout.TotalMilliseconds);

        public static int Poll(Span<Pollfd> fds, int timeoutMillis) => Poll(fds, (nuint)fds.Length, timeoutMillis);

        [LibraryImport(LibraryName, EntryPoint = "close", SetLastError = true)]
        public static partial int Close(int fd);
    }
}
