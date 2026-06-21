namespace LoupixDeck.Native;

public sealed class FileDescriptor(string path, FileAccess flags, bool blocking) : FileDescriptorBase(path, flags, blocking)
{
    public static FileDescriptor Open(string path, FileAccess flags, bool blocking) => new(path, flags, blocking);

    public new void Write(ReadOnlySpan<byte> buffer) => base.Write(buffer);
    public new bool TryWrite(ReadOnlySpan<byte> buffer, out long bytesWritten) => base.TryWrite(buffer, out bytesWritten);
}
