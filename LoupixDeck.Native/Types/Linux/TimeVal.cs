using System.Runtime.InteropServices;

namespace LoupixDeck.Native.Types.Linux;

[StructLayout(LayoutKind.Sequential, Size = ByteSize)]
public struct TimeVal
{
    public const int ByteSize = sizeof(long) * 2;
    public long tv_sec;
    public long tv_usec;
}
