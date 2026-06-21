using System.Runtime.Versioning;

namespace LoupixDeck.Native;

[SupportedOSPlatform("linux")]
public sealed class FileDescriptor(string path, FileAccess flags, bool blocking) : FileDescriptorBase(path, flags, blocking)
{
    public static FileDescriptor Open(string path, FileAccess flags, bool blocking = false) => new(path, flags, blocking);

    public new bool TryRead(Span<byte> buffer, out long bytesWritten) => base.TryRead(buffer, out bytesWritten);
    public new void Write(ReadOnlySpan<byte> buffer) => base.Write(buffer);
    public new bool TryWrite(ReadOnlySpan<byte> buffer, out long bytesWritten) => base.TryWrite(buffer, out bytesWritten);
}
