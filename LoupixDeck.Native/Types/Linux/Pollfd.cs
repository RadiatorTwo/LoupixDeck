using System.Runtime.InteropServices;

namespace LoupixDeck.Native.Types.Linux;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Pollfd
{
    public int fd;
    public short events;
    public short revents;
}
